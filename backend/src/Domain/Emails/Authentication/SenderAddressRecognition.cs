// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails.Signatures;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What recognizing the address a message's <c>From</c> displays grants that message, whichever source below the organization's signature recognizes it.</summary>
/// <remarks>
/// <para>
/// Every one of those sources is held to the same rule, so the answer depends on the message and on whether the account
/// was strict, never on which source holds the address. It is <see cref="SenderTrustLevel.Trusted" /> where something
/// tied the message to the address: the author authenticated by a DMARC <c>pass</c> or an aligned DKIM signature, never
/// by SPF alone, or the message's own signature verified and names the author. Where nothing did, a relaxed account
/// still recognizes the address as <see cref="SenderTrustLevel.Known" />, unless the receiving server reported a DMARC
/// <c>fail</c> under a policy that does more than monitor.
/// </para>
/// <para>
/// It is reached from what the message stores, so the same answer is asked of the owner's own address and domain when the
/// message is stored and of the member and the contacts that hold the address whenever it is read.
/// </para>
/// </remarks>
public sealed record SenderAddressRecognition
{
    private SenderAddressRecognition(TrustedAuthorEvidence evidence, bool knowsByAddressAlone)
    {
        this.Evidence = evidence;
        this.KnowsByAddressAlone = knowsByAddressAlone;
    }

    /// <summary>Gets what tied the message to the address, or <see cref="TrustedAuthorEvidence.None" /> where nothing did.</summary>
    public TrustedAuthorEvidence Evidence { get; }

    /// <summary>Gets whether the address alone still recognizes the author where nothing tied the message to it.</summary>
    public bool KnowsByAddressAlone { get; }

    /// <summary>Reaches the answer from what extracting a message established.</summary>
    /// <param name="authentication">What the receiving mail server, or local verification, established about the message.</param>
    /// <param name="signature">What verifying the message's own S/MIME or OpenPGP signature found.</param>
    /// <param name="senderDomain">The domain of the first <c>From</c> mailbox.</param>
    /// <param name="isStrict">Whether the receiving account recognizes only authors something tied to the address.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static SenderAddressRecognition Of(
        SenderAuthentication authentication,
        EmailSignature signature,
        SenderDomain senderDomain,
        bool isStrict)
    {
        ArgumentNullException.ThrowIfNull(authentication);

        return Of(
            authentication.AuthorAuthentication,
            authentication.AuthorAuthenticationRoute,
            authentication.AuthenticatedAuthorDomain,
            authentication.DmarcPolicy,
            signature,
            senderDomain,
            isStrict);
    }

    /// <summary>Reaches the answer from the parts of a message's authentication a stored row keeps.</summary>
    /// <param name="authorAuthentication">Whether the author was established, refused, or neither.</param>
    /// <param name="route">What established an authenticated author.</param>
    /// <param name="authenticatedAuthorDomain">The domain the author was authenticated for, where one was.</param>
    /// <param name="dmarcPolicy">The policy the author's domain publishes, as read when the message was stored.</param>
    /// <param name="signature">What verifying the message's own S/MIME or OpenPGP signature found.</param>
    /// <param name="senderDomain">The domain of the first <c>From</c> mailbox.</param>
    /// <param name="isStrict">Whether the receiving account recognizes only authors something tied to the address.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signature" /> is <see langword="null" />.</exception>
    public static SenderAddressRecognition Of(
        AuthorAuthenticationOutcome authorAuthentication,
        AuthorAuthenticationRoute route,
        SenderDomain? authenticatedAuthorDomain,
        DmarcPolicyReading dmarcPolicy,
        EmailSignature signature,
        SenderDomain senderDomain,
        bool isStrict)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var evidence = authenticatedAuthorDomain == senderDomain
            && route is AuthorAuthenticationRoute.DmarcPass or AuthorAuthenticationRoute.AlignedDkim
                ? TrustedAuthorEvidence.Authentication
                : signature is { Outcome: EmailSignatureOutcome.Verified, SignerMatchesAuthor: true }
                    ? TrustedAuthorEvidence.AuthorSignature
                    : TrustedAuthorEvidence.None;

        // A server's DMARC fail is the domain's own refusal and stays one, except where the domain publishes a policy
        // that asks only to be monitored: its owner then admits that some of their legitimate mail fails, which is
        // exactly the mail a relaxed account accepts.
        var knowsByAddressAlone = !isStrict
            && (authorAuthentication != AuthorAuthenticationOutcome.Failed
                || dmarcPolicy == DmarcPolicyReading.MonitoringOnly);

        return new SenderAddressRecognition(evidence, knowsByAddressAlone);
    }

    /// <summary>States what one source holding the address makes of the author, or <see langword="null" /> where it makes nothing of them.</summary>
    internal SenderTrust? Grant(
        SenderTrustSource source,
        ContactId? contact,
        SenderTrustPolicyRevision revision,
        bool isStrict)
    {
        if (this.Evidence != TrustedAuthorEvidence.None)
        {
            return contact is { } contactId
                ? SenderTrust.TrustedByContact(source, contactId, this.Evidence, revision, isStrict)
                : SenderTrust.TrustedAsOwn(source, this.Evidence, revision, isStrict);
        }

        return this.KnowsByAddressAlone ? SenderTrust.Known(source, contact, revision) : null;
    }

    /// <summary>States what the member directory naming the address makes of the author, or <see langword="null" /> where it makes nothing of them.</summary>
    internal SenderTrust? GrantByMember(UserId member, SenderTrustPolicyRevision revision, bool isStrict) =>
        this.Evidence != TrustedAuthorEvidence.None ? SenderTrust.TrustedByMember(member, this.Evidence, revision, isStrict)
            : this.KnowsByAddressAlone ? SenderTrust.KnownMember(member, revision)
            : null;
}
