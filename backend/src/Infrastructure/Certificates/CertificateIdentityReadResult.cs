// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Infrastructure.Certificates;

/// <summary>A certificate paired with its private key and the certificates supplied beside it, or why the material produced none.</summary>
/// <remarks>
/// Nothing here has been judged for a purpose yet: whether the leaf can serve a domain or sign as a mailbox is the
/// question of whichever loader asked, so the supplied certificates arrive unordered and unchecked.
/// </remarks>
internal sealed record CertificateIdentityReadResult
{
    private CertificateIdentityReadResult(
        X509Certificate2? leaf,
        X509Certificate2[] supplied,
        CertificateMaterialFailure? failure)
    {
        this.Leaf = leaf;
        this.Supplied = supplied;
        this.Failure = failure;
    }

    /// <summary>Gets the certificate carrying the private key, owned by the caller, or <see langword="null" /> when reading failed.</summary>
    internal X509Certificate2? Leaf { get; }

    /// <summary>Gets the other certificates the material carried, owned by the caller, in the order they were read.</summary>
    internal X509Certificate2[] Supplied { get; }

    /// <summary>Gets why no identity was read, or <see langword="null" /> when reading succeeded.</summary>
    internal CertificateMaterialFailure? Failure { get; }

    /// <summary>Creates a successful result that hands ownership of every certificate to the caller.</summary>
    /// <param name="leaf">The certificate carrying the private key.</param>
    /// <param name="supplied">The other certificates the material carried.</param>
    /// <returns>The successful result.</returns>
    internal static CertificateIdentityReadResult Read(X509Certificate2 leaf, X509Certificate2[] supplied) =>
        new(leaf, supplied, failure: null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="failure">Why no identity was read.</param>
    /// <returns>The failed result.</returns>
    internal static CertificateIdentityReadResult Failed(CertificateMaterialFailure failure) =>
        new(leaf: null, [], failure);
}
