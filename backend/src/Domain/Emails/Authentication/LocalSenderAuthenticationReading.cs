// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Reads the verdict a message's own DKIM signatures support, where no trusted server said anything.</summary>
/// <remarks>
/// <para>
/// It is the fallback half of <see cref="SenderAuthenticationReading" /> and deliberately mirrors it: the verifying
/// happens outside — one signature at a time, against a key its domain publishes — and what to make of the results is
/// decided here, where it is unit-testable without a network. The two readings never both run on one message, because
/// an account whose receiving server writes the header goes on believing that server.
/// </para>
/// <para>
/// A locally reached verdict is narrower than a server-written one and the narrowing is structural rather than
/// incidental. SPF authenticates an envelope sender against a connecting address and after delivery there is neither,
/// so no SPF identity is ever produced here; a DMARC result needs SPF as well, so none is reported either. What is left
/// is the one check the stored bytes still answer, and it is the stronger of the two: a key the signing domain
/// publishes signed exactly these octets.
/// </para>
/// <para>
/// The author conclusion follows from the same rule the trusted reading uses and is neither relaxed nor tightened for
/// running here. An author is established by a verified signature whose domain is the displayed one or is one within
/// the other at a label boundary, narrowed to equality where the displayed domain's DMARC record asks for
/// <c>adkim=s</c>, and not at all where that record could not be read. It is never failed, under any policy the
/// record states: a receiving server that enforces DMARC never delivers a <c>reject</c> failure, so a message here that
/// does not align almost always passed at the server through SPF, or was rewritten after delivery.
/// </para>
/// </remarks>
public static class LocalSenderAuthenticationReading
{
    /// <summary>How many of a message's DKIM signatures are verified before the rest are passed over.</summary>
    /// <remarks>
    /// Each signature costs a key lookup, so an attacker writing hundreds of them into one message would otherwise buy
    /// hundreds of DNS queries per delivery from a mailbox that has to parse whatever arrives. Ordinary mail carries one
    /// or two — an author's and a delivery provider's — so the bound is far above what a legitimate message uses and
    /// far below what one would cost.
    /// </remarks>
    public const int MaximumVerifiedSignaturesPerMessage = 8;

    /// <summary>Reads the verdict one message's locally checked signatures support.</summary>
    /// <param name="verifiedSigningDomains">The domain of every signature that verified, in the order the message carried them.</param>
    /// <param name="anySignatureRejected">
    /// Whether a signature was checked against a key that resolved and did not verify. It separates a failure from
    /// silence, exactly as an attempted-and-failed method does in the trusted reading: a key that could not be resolved
    /// is not a rejection and must not be reported as one.
    /// </param>
    /// <param name="displayedSenderAddress">The address the message's <c>From</c> header wrote, where it wrote one.</param>
    /// <param name="dmarcRecord">
    /// What looking for the displayed domain's DMARC record found, where <see cref="DomainWhoseDmarcRecordDecides" />
    /// asked for it, and <see cref="DmarcRecordLookup.NotReadable" /> where nothing was looked up.
    /// </param>
    /// <returns>The verdict, which names <see cref="SenderAuthenticationSource.LocalVerification" /> whatever it says.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="verifiedSigningDomains" /> or <paramref name="dmarcRecord" /> is <see langword="null" />.</exception>
    public static SenderAuthentication Read(
        IReadOnlyList<SenderDomain> verifiedSigningDomains,
        bool anySignatureRejected,
        string? displayedSenderAddress,
        DmarcRecordLookup dmarcRecord)
    {
        ArgumentNullException.ThrowIfNull(verifiedSigningDomains);
        ArgumentNullException.ThrowIfNull(dmarcRecord);

        var fromDomain = DisplayedDomainOf(displayedSenderAddress);

        if (verifiedSigningDomains.Count > 0)
        {
            return SenderAuthentication.LocallyVerified(verifiedSigningDomains, fromDomain, dmarcRecord);
        }

        return anySignatureRejected
            ? SenderAuthentication.LocalVerificationFailed(fromDomain)
            : SenderAuthentication.LocalVerificationNotEstablished(fromDomain);
    }

    /// <summary>Names the domain whose DMARC record decides whether a verified signature aligns, where one could.</summary>
    /// <param name="verifiedSigningDomains">The domain of every signature that verified, in the order the message carried them.</param>
    /// <param name="displayedSenderAddress">The address the message's <c>From</c> header wrote, where it wrote one.</param>
    /// <returns>
    /// The displayed domain, where a verified signing name lies on its branch of the naming tree; otherwise
    /// <see langword="null" />, since no record could make any of them aligned and no lookup is worth making.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="verifiedSigningDomains" /> is <see langword="null" />.</exception>
    public static SenderDomain? DomainWhoseDmarcRecordDecides(
        IReadOnlyList<SenderDomain> verifiedSigningDomains,
        string? displayedSenderAddress)
    {
        ArgumentNullException.ThrowIfNull(verifiedSigningDomains);

        return DisplayedDomainOf(displayedSenderAddress) is { } fromDomain
            && verifiedSigningDomains.Any(signing => signing.SharesBranchWith(fromDomain))
            ? fromDomain
            : null;
    }

    private static SenderDomain? DisplayedDomainOf(string? displayedSenderAddress) =>
        SenderDomain.TryCreateFromMailbox(displayedSenderAddress, out var displayed) ? displayed : null;
}
