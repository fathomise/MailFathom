// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Infrastructure.Mail.Signing;

/// <summary>Configures the S/MIME certificate, and its private key, that one account signs its outgoing mail with.</summary>
/// <remarks>
/// <para>
/// It takes the two provisioning shapes <see cref="TlsServerCertificateOptions" /> takes, for the reason it does: a
/// certificate authority hands out a PEM chain beside a separate key, and an export from a mail client or a key store
/// is one PKCS#12 bundle holding both. Exactly one of them is configured, and both are read by the same reader.
/// </para>
/// <para>
/// Every part binds to <see cref="ConfiguredSecret" />, the public chain included, so each is a reference an operator
/// points at a file, a systemd credential, or an environment variable, and secret discovery proves at startup that it
/// is reachable.
/// </para>
/// </remarks>
public sealed class SmimeSigningCertificateOptions
{
    /// <summary>Gets or sets the PKCS#12/PFX bundle holding the signing certificate, its private key, and any intermediates, whose nested password block opens it when it is protected.</summary>
    public ConfiguredSecret? Bundle { get; set; }

    /// <summary>Gets or sets the PEM certificate material, whose first certificate is the signing certificate and whose remaining certificates are the chain sent with each signature.</summary>
    public ConfiguredSecret? CertificateChain { get; set; }

    /// <summary>Gets or sets the PEM private key belonging to the signing certificate, whose nested password block decrypts it when it is encrypted.</summary>
    public ConfiguredSecret? PrivateKey { get; set; }

    /// <summary>Gets whether the block names any material at all.</summary>
    /// <remarks>A block naming none is an account that signs nothing with S/MIME, which is the ordinary shape rather than a misconfiguration.</remarks>
    public bool IsConfigured => IsBlockConfigured(this.Bundle)
        || IsBlockConfigured(this.CertificateChain)
        || IsBlockConfigured(this.PrivateKey);

    /// <summary>Finds everything an operator must fix before this material could be read at all.</summary>
    /// <param name="configurationPath">The configuration path of this block, which prefixes every reported error.</param>
    /// <returns>One message per faulty setting, empty when the block names nothing or names one provisioning shape completely.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Only the shape is decided here. Whether the material loads, carries its key, may sign mail, and names the
    /// account's sending address is <see cref="SmimeSigningCertificateValidator" />'s question, answered against the
    /// material itself.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors(string configurationPath)
    {
        ArgumentNullException.ThrowIfNull(configurationPath);

        var bundleConfigured = IsBlockConfigured(this.Bundle);
        var chainConfigured = IsBlockConfigured(this.CertificateChain);
        var privateKeyConfigured = IsBlockConfigured(this.PrivateKey);

        if (bundleConfigured && (chainConfigured || privateKeyConfigured))
        {
            return [$"{configurationPath} — a PKCS#12 bundle and separate PEM material are both configured; state one or the other, because which of them supplies the signing identity would otherwise be decided by nothing an operator wrote."];
        }

        if (bundleConfigured || (!chainConfigured && !privateKeyConfigured))
        {
            return [];
        }

        if (!chainConfigured)
        {
            return [$"{configurationPath}:{nameof(this.CertificateChain)} — a private key is configured with no certificate to pair it with."];
        }

        return privateKeyConfigured
            ? []
            : [$"{configurationPath}:{nameof(this.PrivateKey)} — a certificate is configured with no private key, so nothing could be signed with it."];
    }

    private static bool IsBlockConfigured(ConfiguredSecret? block) =>
        block is not null && !string.IsNullOrWhiteSpace(block.SecretReference);
}
