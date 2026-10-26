// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Infrastructure.Certificates;

/// <summary>Reads a certificate and its private key out of a PKCS#12 bundle, or out of a PEM chain beside a PEM key.</summary>
/// <remarks>
/// <para>
/// Every identity MailFathom holds a private key for is provisioned in one of those two shapes, so the reading is one
/// piece of code whichever purpose the identity serves. What it serves is judged afterwards by the loader that asked:
/// a TLS identity against a domain, an S/MIME signing identity against a mailbox.
/// </para>
/// <para>
/// Every private key is imported with <see cref="X509KeyStorageFlags.EphemeralKeySet" />, so provisioning one never
/// leaves a copy in an operating-system key store, and every key and password is revealed only into a pinned buffer
/// erased before the method that filled it returns.
/// </para>
/// </remarks>
internal sealed class CertificateIdentityReader
{
    private readonly ISecretReferenceResolver secretReferenceResolver;

    /// <summary>Initializes a new reader.</summary>
    /// <param name="secretReferenceResolver">The resolver that turns a configured reference into material.</param>
    internal CertificateIdentityReader(ISecretReferenceResolver secretReferenceResolver)
    {
        this.secretReferenceResolver = secretReferenceResolver;
    }

    /// <summary>Reads the identity one of the two provisioning shapes names.</summary>
    /// <param name="bundle">The PKCS#12 bundle, whose nested password block opens it.</param>
    /// <param name="certificateChain">The PEM certificate material, whose first certificate is the leaf.</param>
    /// <param name="privateKey">The PEM private key of the leaf, whose nested password block decrypts it.</param>
    /// <param name="cancellationToken">Cancels the material retrieval and every password retrieval it needs.</param>
    /// <returns>The leaf with its private key and the certificates supplied beside it, owned by the caller, or a named failure.</returns>
    internal async Task<CertificateIdentityReadResult> ReadAsync(
        ConfiguredSecret? bundle,
        ConfiguredSecret? certificateChain,
        ConfiguredSecret? privateKey,
        CancellationToken cancellationToken)
    {
        var bundleConfigured = IsConfigured(bundle);
        var pemConfigured = IsConfigured(certificateChain) || IsConfigured(privateKey);

        if (bundleConfigured && pemConfigured)
        {
            return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.MaterialKindAmbiguous);
        }

        if (bundleConfigured)
        {
            return await this.ReadBundleAsync(bundle!, cancellationToken);
        }

        if (pemConfigured)
        {
            return await this.ReadPemAsync(certificateChain, privateKey, cancellationToken);
        }

        return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.MaterialMissing);
    }

    private static bool IsConfigured(ConfiguredSecret? block) =>
        block is not null && !string.IsNullOrWhiteSpace(block.SecretReference);

    private async Task<CertificateIdentityReadResult> ReadBundleAsync(
        ConfiguredSecret configuredBundle,
        CancellationToken cancellationToken)
    {
        var bundleResult = await this.secretReferenceResolver.ResolveAsync(
            configuredBundle.SecretReference,
            cancellationToken);

        if (bundleResult.Secret is not { } bundleMaterial)
        {
            return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
        }

        using (bundleMaterial)
        {
            // A PKCS#12 bundle is binary and has no faithful representation in a configuration value, so material that
            // arrived as the configured value itself cannot be one however it parses.
            if (bundleResult.Source == SecretMaterialSource.InlineValue)
            {
                return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.InlineEncodingNotSupported);
            }

            var encoding = CertificateMaterialEncodingDetector.Detect(bundleMaterial.RevealBytes());

            if (encoding == CertificateMaterialEncoding.Unrecognized)
            {
                return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.EncodingNotRecognized);
            }

            if (encoding != CertificateMaterialEncoding.Pkcs12)
            {
                return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.EncodingNotSupportedForRole);
            }

            var passwordResult = await this.ResolvePasswordAsync(configuredBundle.Password, cancellationToken);

            using (passwordResult.Password)
            {
                if (!passwordResult.Resolvable)
                {
                    return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
                }

                return OpenBundle(bundleMaterial, passwordResult.Password);
            }
        }
    }

    /// <summary>Opens the bundle with the password revealed into a buffer this method erases before it returns.</summary>
    /// <remarks>
    /// The buffer is allocated pinned and erased in the same method that fills it, so the bundle password never reaches
    /// an un-erasable <see cref="string" />. The platform reports a wrong password, a missing one, and corrupt contents
    /// through one exception, so the two failures are told apart by what was configured rather than by what was
    /// reported.
    /// </remarks>
    private static CertificateIdentityReadResult OpenBundle(
        ResolvedSecret bundleMaterial,
        ResolvedSecret? bundlePassword)
    {
        var passwordBuffer = bundlePassword is null
            ? []
            : GC.AllocateArray<char>(bundlePassword.TextLength, pinned: true);

        try
        {
            bundlePassword?.RevealTextInto(passwordBuffer);

            X509Certificate2Collection bundle;
            try
            {
                bundle = X509CertificateLoader.LoadPkcs12Collection(
                    bundleMaterial.RevealBytes(),
                    passwordBuffer,
                    X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (CryptographicException)
            {
                return CertificateIdentityReadResult.Failed(bundlePassword is not null
                    ? CertificateMaterialFailure.BundlePasswordIncorrect
                    : CertificateMaterialFailure.BundlePasswordMissing);
            }

            return SplitBundle(bundle);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(passwordBuffer.AsSpan()));
        }
    }

    /// <summary>Picks the identity out of a bundle by which certificate holds a private key.</summary>
    /// <remarks>
    /// Position is not used, because export tools disagree about ordering. Exactly one key-bearing certificate is the
    /// identity and the rest are the chain; several make which identity is presented depend on parse order.
    /// </remarks>
    private static CertificateIdentityReadResult SplitBundle(X509Certificate2Collection bundle)
    {
        if (bundle.Count == 0)
        {
            return DisposeAndFail(bundle, CertificateMaterialFailure.BundleCarriesNoCertificate);
        }

        var identities = bundle.Where(static candidate => candidate.HasPrivateKey).ToArray();

        if (identities.Length == 0)
        {
            return DisposeAndFail(bundle, CertificateMaterialFailure.PrivateKeyMissing);
        }

        if (identities.Length > 1)
        {
            return DisposeAndFail(bundle, CertificateMaterialFailure.ChainCarriesSeveralLeaves);
        }

        var leaf = identities[0];

        return CertificateIdentityReadResult.Read(leaf, [.. bundle.Where(candidate => !ReferenceEquals(candidate, leaf))]);
    }

    private async Task<CertificateIdentityReadResult> ReadPemAsync(
        ConfiguredSecret? configuredChain,
        ConfiguredSecret? configuredPrivateKey,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured(configuredChain))
        {
            return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.MaterialMissing);
        }

        if (!IsConfigured(configuredPrivateKey))
        {
            return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.PrivateKeyMissing);
        }

        var chainResult = await this.secretReferenceResolver.ResolveAsync(
            configuredChain!.SecretReference,
            cancellationToken);

        if (chainResult.Secret is not { } chainMaterial)
        {
            return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
        }

        using (chainMaterial)
        {
            var keyResult = await this.secretReferenceResolver.ResolveAsync(
                configuredPrivateKey!.SecretReference,
                cancellationToken);

            if (keyResult.Secret is not { } keyMaterial)
            {
                return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
            }

            using (keyMaterial)
            {
                var keyPasswordResult = await this.ResolvePasswordAsync(
                    configuredPrivateKey.Password,
                    cancellationToken);

                using (keyPasswordResult.Password)
                {
                    if (!keyPasswordResult.Resolvable)
                    {
                        return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
                    }

                    return ReadPem(chainMaterial, keyMaterial, keyPasswordResult.Password);
                }
            }
        }
    }

    /// <summary>Parses the PEM chain and pairs its leaf with the configured private key.</summary>
    /// <remarks>
    /// The chain is public material and is decoded into an ordinary buffer, while the key and its password are decoded
    /// into pinned buffers erased before the pairing returns.
    /// </remarks>
    private static CertificateIdentityReadResult ReadPem(
        ResolvedSecret chainMaterial,
        ResolvedSecret keyMaterial,
        ResolvedSecret? keyPassword)
    {
        if (CertificateMaterialEncodingDetector.Detect(chainMaterial.RevealBytes()) is var chainEncoding
            && chainEncoding != CertificateMaterialEncoding.Pem)
        {
            return CertificateIdentityReadResult.Failed(chainEncoding == CertificateMaterialEncoding.Unrecognized
                ? CertificateMaterialFailure.EncodingNotRecognized
                : CertificateMaterialFailure.EncodingNotSupportedForRole);
        }

        var chainText = new char[chainMaterial.TextLength];
        chainMaterial.RevealTextInto(chainText);

        var chain = new X509Certificate2Collection();
        try
        {
            chain.ImportFromPem(chainText);
        }
        catch (CryptographicException)
        {
            return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.MaterialNotReadable);
        }

        if (chain.Count == 0)
        {
            return CertificateIdentityReadResult.Failed(CertificateMaterialFailure.MaterialNotReadable);
        }

        var parsedLeaf = chain[0];

        if (chain.Count(candidate => string.Equals(candidate.Subject, parsedLeaf.Subject, StringComparison.Ordinal)) > 1)
        {
            return DisposeAndFail(chain, CertificateMaterialFailure.ChainCarriesSeveralLeaves);
        }

        return PairWithPrivateKey(chainText, keyMaterial, keyPassword, chain);
    }

    /// <summary>Attaches the configured private key to the parsed leaf, revealing the key only into buffers it erases.</summary>
    /// <remarks>
    /// The platform reports a malformed key and a key belonging to a different certificate through one exception, so
    /// the key is parsed on its own afterwards to tell the two apart. That distinction is worth the second parse: one
    /// failure means the file is damaged and the other means the wrong file was provisioned, and an operator fixes them
    /// differently.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the paired leaf passes to the caller through the result.")]
    private static CertificateIdentityReadResult PairWithPrivateKey(
        char[] chainText,
        ResolvedSecret keyMaterial,
        ResolvedSecret? keyPassword,
        X509Certificate2Collection chain)
    {
        var keyText = GC.AllocateArray<char>(keyMaterial.TextLength, pinned: true);
        var keyPasswordText = keyPassword is null
            ? []
            : GC.AllocateArray<char>(keyPassword.TextLength, pinned: true);

        try
        {
            keyMaterial.RevealTextInto(keyText);
            keyPassword?.RevealTextInto(keyPasswordText);

            X509Certificate2 leafWithPrivateKey;
            try
            {
                leafWithPrivateKey = keyPassword is null
                    ? X509Certificate2.CreateFromPem(chainText, keyText)
                    : X509Certificate2.CreateFromEncryptedPem(chainText, keyText, keyPasswordText);
            }
            catch (Exception failure) when (failure is CryptographicException or ArgumentException)
            {
                // Both are caught because the platform reaches the same outcome through two paths: material it cannot
                // read is a cryptographic failure, while a well-formed key belonging elsewhere surfaces from
                // CopyWithPrivateKey as an argument failure. Neither exception says which happened, so the key is
                // parsed on its own to decide.
                return DisposeAndFail(chain, PrivateKeyParses(keyText, keyPasswordText, keyPassword is not null)
                    ? CertificateMaterialFailure.PrivateKeyDoesNotMatchCertificate
                    : CertificateMaterialFailure.PrivateKeyNotReadable);
            }

            var supplied = chain.Skip(1).ToArray();
            chain[0].Dispose();

            return CertificateIdentityReadResult.Read(leafWithPrivateKey, supplied);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(keyText.AsSpan()));
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(keyPasswordText.AsSpan()));
        }
    }

    /// <summary>Tells a damaged private key apart from one that simply belongs elsewhere.</summary>
    /// <remarks>
    /// The three algorithms are tried rather than inferred from the certificate, because a key that parses as any of
    /// them is a well-formed key and the pairing failure was therefore a mismatch. Each import is attempted on its own
    /// instance, since a failed import leaves the algorithm object in an unspecified state.
    /// </remarks>
    private static bool PrivateKeyParses(
        ReadOnlySpan<char> keyText,
        ReadOnlySpan<char> keyPassword,
        bool encrypted)
    {
        return ImportsInto(RSA.Create(), keyText, keyPassword, encrypted)
            || ImportsInto(ECDsa.Create(), keyText, keyPassword, encrypted)
            || ImportsInto(DSA.Create(), keyText, keyPassword, encrypted);
    }

    private static bool ImportsInto(
        AsymmetricAlgorithm algorithm,
        ReadOnlySpan<char> keyText,
        ReadOnlySpan<char> keyPassword,
        bool encrypted)
    {
        using (algorithm)
        {
            try
            {
                if (encrypted)
                {
                    algorithm.ImportFromEncryptedPem(keyText, keyPassword);
                }
                else
                {
                    algorithm.ImportFromPem(keyText);
                }

                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }

    private async Task<(bool Resolvable, ResolvedSecret? Password)> ResolvePasswordAsync(
        ConfiguredSecret? configuredPassword,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured(configuredPassword))
        {
            return (Resolvable: true, Password: null);
        }

        var result = await this.secretReferenceResolver.ResolveAsync(
            configuredPassword!.SecretReference,
            cancellationToken);

        return (result.Succeeded, result.Secret);
    }

    private static CertificateIdentityReadResult DisposeAndFail(
        X509Certificate2Collection parsed,
        CertificateMaterialFailure failure)
    {
        foreach (var certificate in parsed)
        {
            certificate.Dispose();
        }

        return CertificateIdentityReadResult.Failed(failure);
    }
}
