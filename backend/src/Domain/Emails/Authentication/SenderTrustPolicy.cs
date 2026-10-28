// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails.Signatures;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Decides whether one account recognizes the author of a message it received.</summary>
/// <remarks>
/// <para>
/// The rule is deliberately narrow. An author is recognized when a signature of the receiving account's organization
/// proved them a member of it, when a contact somebody wrote down holds their address, when they write as one of the
/// addresses the receiving account's owner holds, or when the receiving account opted into its owner's domains and
/// theirs is one of them. Everything else is unknown, including most legitimate mail, because the claim being published
/// is <em>this deployment does not know who wrote this</em> rather than <em>this is suspicious</em>.
/// </para>
/// <para>
/// <b>The owner's own addresses always count, and every one of their accounts' addresses, not only the receiving
/// one's.</b> Mail from a person to themselves, between their work mailbox and their personal one, is the least
/// suspicious mail in either. <b>The owner's domains count only where the receiving account asked for them</b>, because
/// on a large shared provider every stranger writes from the owner's domain.
/// </para>
/// <para>
/// <b>An account that is not strict also recognizes a sender by address alone</b>, as <see cref="SenderTrustLevel.Known" />
/// rather than trusted, wherever nothing tied the message to that address. Every account is relaxed unless it asks to
/// be strict, because the domains of most real correspondents never authenticate in a way this deployment can see.
/// </para>
/// <para>
/// Contacts are looked up per message rather than held here, so the revision digests what configuration states — the
/// owner's addresses and domains, whether the account is strict, and the receiving organization — and the contact that
/// recognized an author is named by the verdict instead.
/// </para>
/// </remarks>
public sealed class SenderTrustPolicy
{
    private readonly HashSet<EmailAddress> ownAccountAddresses;
    private readonly HashSet<SenderDomain> ownAccountDomains;
    private readonly string[] statements;

    private SenderTrustPolicy(
        HashSet<EmailAddress> ownAccountAddresses,
        HashSet<SenderDomain> ownAccountDomains,
        bool isStrict)
    {
        this.ownAccountAddresses = ownAccountAddresses;
        this.ownAccountDomains = ownAccountDomains;
        this.IsStrict = isStrict;

        this.statements =
        [
            .. ownAccountAddresses.Select(static address => $"own:{address.NormalizedAddress}"),
            .. ownAccountDomains.Select(static domain => $"account:{domain.NormalizedValue}"),
            .. isStrict ? ["strict"] : Array.Empty<string>(),
        ];
        this.Revision = SenderTrustPolicyRevision.Of(this.statements);
    }

    /// <summary>Gets the policy that knows nothing of its own, which is what an account this deployment no longer serves has.</summary>
    /// <remarks>
    /// It carries <see cref="SenderTrustPolicyRevision.None" />, states no address and no domain, and is relaxed as every
    /// account is by default. The contacts a caller looked up still count under it, because they are the people who
    /// wrote them down rather than this account's configuration.
    /// </remarks>
    public static SenderTrustPolicy RecognizingNobody { get; } = new([], [], isStrict: false);

    /// <summary>Gets whether the account recognizes only authors something tied to the address, and so never produces <see cref="SenderTrustLevel.Known" />.</summary>
    public bool IsStrict { get; }

    /// <summary>Gets the revision that names this policy, which a verdict reached without an organization verification is stored with.</summary>
    /// <remarks>A verdict reached for an account of an organization carries a revision that also names that organization, so it differs from this one.</remarks>
    public SenderTrustPolicyRevision Revision { get; }

    /// <summary>Builds the policy one account judges its mail by.</summary>
    /// <param name="ownAccountAddresses">The addresses the receiving account's owner holds across all of their accounts.</param>
    /// <param name="ownAccountDomains">The domains of the owner's accounts where the receiving account opted into them, or none where it did not.</param>
    /// <param name="isStrict">Whether the account recognizes only authors something tied to the address, which an account asks for and none has by default.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static SenderTrustPolicy Create(
        IEnumerable<EmailAddress> ownAccountAddresses,
        IEnumerable<SenderDomain> ownAccountDomains,
        bool isStrict = false)
    {
        ArgumentNullException.ThrowIfNull(ownAccountAddresses);
        ArgumentNullException.ThrowIfNull(ownAccountDomains);

        return new SenderTrustPolicy([.. ownAccountAddresses], [.. ownAccountDomains], isStrict);
    }

    /// <summary>Decides what this deployment makes of one message's author.</summary>
    /// <param name="authentication">What the receiving mail server, or local verification, established about the message.</param>
    /// <param name="signature">What verifying the message's own S/MIME or OpenPGP signature found.</param>
    /// <param name="displayedSender">The first mailbox of the message's <c>From</c> header, where it wrote a usable one.</param>
    /// <param name="contacts">The contacts that hold <paramref name="displayedSender" /> and may make it trusted.</param>
    /// <param name="organizationVerification">
    /// What verifying the message against the receiving account's organization established, or <see langword="null" />
    /// where the account belongs to no organization and nothing was verified.
    /// </param>
    /// <returns>
    /// The verdict, carrying <see cref="Revision" />, or a revision that also names the receiving organization where
    /// <paramref name="organizationVerification" /> was given, so a verdict says whose keys it was reached under.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// <b>The sources are asked in order, highest first</b>: organization member, organization contact, the owner's
    /// address, the owner's domain, a personal contact. Each is asked only where the one above did not recognize the
    /// author, so a sender several of them recognize is reported under the highest.
    /// </para>
    /// <para>
    /// <b>Organization membership needs nothing else.</b> A signature of the receiving account's organization proves the
    /// author's address and the recipient, which is more than anything below establishes.
    /// </para>
    /// <para>
    /// <b>Every other source needs the message tied to the address first</b>, because <c>From</c> on its own is written
    /// by whoever sent the message. Two things tie it, asked in this order: the author authenticated by the receiving
    /// server's DMARC <c>pass</c> or by an aligned DKIM signature, never by SPF alone; or the message's own signature
    /// verified and names the author, which holds even where the receiving server reported DMARC <c>fail</c>, because a
    /// forward or a list that broke the domain's checks leaves it intact.
    /// </para>
    /// <para>
    /// <b>Where neither holds, a relaxed account still asks the same sources of the address alone</b>, in the same order,
    /// and a match is <see cref="SenderTrustLevel.Known" />. A server's DMARC <c>fail</c> stops that unless the domain's
    /// record asks only to be monitored, and a strict account never asks.
    /// </para>
    /// </remarks>
    public SenderTrust Evaluate(
        SenderAuthentication authentication,
        EmailSignature signature,
        EmailAddress? displayedSender,
        RecognizingContacts contacts,
        OrganizationSenderVerification? organizationVerification = null)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(contacts);

        var revision = organizationVerification is { ReceivingOrganizationId: var organizationId }
            ? SenderTrustPolicyRevision.Of([.. this.statements, $"organization:{organizationId:D}"])
            : this.Revision;

        if (organizationVerification is { SenderIsMember: true })
        {
            return SenderTrust.TrustedAsOrganizationMember(organizationVerification.ReceivingOrganizationId, revision);
        }

        if (displayedSender is not { } sender
            || !SenderDomain.TryCreateFromMailbox(sender.Address, out var senderDomain))
        {
            return SenderTrust.Unknown(revision);
        }

        var evidence = EvidenceTying(authentication, signature, senderDomain);

        if (this.Recognize(sender, senderDomain, contacts) is not { } recognized)
        {
            return SenderTrust.Unknown(revision);
        }

        var (source, contact) = recognized;

        if (evidence != TrustedAuthorEvidence.None)
        {
            return contact is { } contactId
                ? SenderTrust.TrustedByContact(source, contactId, evidence, revision)
                : SenderTrust.TrustedAsOwn(source, evidence, revision);
        }

        return this.KnowsByAddressAlone(authentication)
            ? SenderTrust.Known(source, contact, revision)
            : SenderTrust.Unknown(revision);
    }

    /// <summary>Answers whether an author nothing tied to the address may still be recognized by it.</summary>
    /// <remarks>
    /// A server's DMARC <c>fail</c> is the domain's own refusal and stays one, except where the domain publishes a
    /// policy that asks only to be monitored: its owner then admits that some of their legitimate mail fails, which is
    /// exactly the mail a relaxed account accepts.
    /// </remarks>
    private bool KnowsByAddressAlone(SenderAuthentication authentication) =>
        !this.IsStrict
        && (authentication.AuthorAuthentication != AuthorAuthenticationOutcome.Failed
            || authentication.DmarcPolicy == DmarcPolicyReading.MonitoringOnly);

    /// <summary>Names the highest source that holds the displayed address, and the contact where that source is one.</summary>
    private (SenderTrustSource Source, ContactId? Contact)? Recognize(
        EmailAddress sender,
        SenderDomain senderDomain,
        RecognizingContacts contacts)
    {
        if (contacts.OrganizationContact is { } organizationContact)
        {
            return (SenderTrustSource.OrganizationContact, organizationContact);
        }

        if (this.ownAccountAddresses.Contains(sender))
        {
            return (SenderTrustSource.OwnAccountAddress, null);
        }

        if (this.ownAccountDomains.Contains(senderDomain))
        {
            return (SenderTrustSource.OwnAccountDomain, null);
        }

        return contacts.PersonalContact is { } personalContact
            ? (SenderTrustSource.PersonalContact, personalContact)
            : null;
    }

    /// <summary>Names what ties the message to the address its <c>From</c> displays, or nothing where nothing does.</summary>
    private static TrustedAuthorEvidence EvidenceTying(
        SenderAuthentication authentication,
        EmailSignature signature,
        SenderDomain senderDomain)
    {
        if (authentication.AuthenticatedAuthorDomain == senderDomain
            && authentication.AuthorAuthenticationRoute is AuthorAuthenticationRoute.DmarcPass or AuthorAuthenticationRoute.AlignedDkim)
        {
            return TrustedAuthorEvidence.Authentication;
        }

        return signature is { Outcome: EmailSignatureOutcome.Verified, SignerMatchesAuthor: true }
            ? TrustedAuthorEvidence.AuthorSignature
            : TrustedAuthorEvidence.None;
    }
}
