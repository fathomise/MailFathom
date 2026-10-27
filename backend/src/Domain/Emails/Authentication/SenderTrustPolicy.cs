// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Decides whether one account recognizes the author a message authenticated as.</summary>
/// <remarks>
/// <para>
/// The rule is deliberately narrow. An author is recognized when a signature of the receiving account's organization
/// proved them a member of it, when they write as one of the addresses the receiving account's owner holds, when the
/// receiving account opted into its owner's domains and their domain is one of them, or when an entry on the receiving
/// account's trusted-sender list names them. Everything else is unknown,
/// including most legitimate mail, because the claim being published is <em>this deployment does not know who wrote
/// this</em> rather than <em>this is suspicious</em>.
/// </para>
/// <para>
/// <b>The owner's own addresses always count, and every one of their accounts' addresses, not only the receiving
/// one's.</b> Mail from a person to themselves, between their work mailbox and their personal one, is the least
/// suspicious mail in either. It is held against an authenticated author exactly as an address entry on the list is,
/// so a <c>From</c> nobody authenticated recognizes nothing however much it looks like the owner.
/// </para>
/// <para>
/// <b>The owner's domains count only where the receiving account asked for them.</b> On a large shared provider every
/// stranger writes from the owner's domain, so the set is opt-in per account, and a policy built for an account that
/// did not opt in simply receives no domains.
/// </para>
/// <para>
/// <b>The trusted-sender list has two halves and this is the one matcher over both.</b> Configuration holds what an
/// operator declared when they set the deployment up and a store holds what somebody added while it was running, an
/// entry in either recognizes, and neither can undo the other — configuration is not editable at runtime and the store
/// is not editable by a configuration reload. The configured half is consulted first, so where both name one sender the
/// verdict reports the deployment's declared trust rather than something added later.
/// </para>
/// </remarks>
public sealed class SenderTrustPolicy
{
    private readonly IReadOnlyList<TrustedSenderEntry> ownAccountAddresses;
    private readonly HashSet<SenderDomain> ownAccountDomains;
    private readonly IReadOnlyList<TrustedSenderEntry> configuredEntries;
    private readonly IReadOnlyList<TrustedSenderEntry> storedEntries;
    private readonly string[] statements;

    private SenderTrustPolicy(
        IReadOnlyList<TrustedSenderEntry> ownAccountAddresses,
        HashSet<SenderDomain> ownAccountDomains,
        IReadOnlyList<TrustedSenderEntry> configuredEntries,
        IReadOnlyList<TrustedSenderEntry> storedEntries)
    {
        this.ownAccountAddresses = ownAccountAddresses;
        this.ownAccountDomains = ownAccountDomains;
        this.configuredEntries = configuredEntries;
        this.storedEntries = storedEntries;

        this.statements =
        [
            .. ownAccountAddresses.Select(static entry => $"own:{entry.ToPolicyStatement()}"),
            .. ownAccountDomains.Select(static domain => $"account:{domain.NormalizedValue}"),
            .. configuredEntries.Select(static entry => $"configured:{entry.ToPolicyStatement()}"),
            .. storedEntries.Select(static entry => $"stored:{entry.ToPolicyStatement()}"),
        ];
        this.Revision = SenderTrustPolicyRevision.Of(this.statements);
    }

    /// <summary>Gets the policy that recognizes nobody, which is what an account this deployment no longer serves has.</summary>
    /// <remarks>
    /// It carries <see cref="SenderTrustPolicyRevision.None" />, so a verdict reached under it is indistinguishable
    /// from one no policy produced — which is the honest reading, since a deployment that has forgotten an account has
    /// no opinion about its mail rather than a negative one.
    /// </remarks>
    public static SenderTrustPolicy RecognizingNobody { get; } = new([], [], [], []);

    /// <summary>Gets the revision that names this policy, which a verdict reached without an organization verification is stored with.</summary>
    /// <remarks>A verdict reached for an account of an organization carries a revision that also names that organization, so it differs from this one.</remarks>
    public SenderTrustPolicyRevision Revision { get; }

    /// <summary>Builds the policy one account judges its mail by.</summary>
    /// <param name="ownAccountAddresses">The addresses the receiving account's owner holds across all of their accounts; one that names no mailbox is skipped.</param>
    /// <param name="ownAccountDomains">The domains of the owner's accounts where the receiving account opted into them, or none where it did not.</param>
    /// <param name="configuredTrustedSenders">What the account's configuration declares it recognizes.</param>
    /// <param name="storedTrustedSenders">What was added to the account's stored list while the deployment was running.</param>
    /// <returns>The policy, which recognizes nobody when all four are empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static SenderTrustPolicy Create(
        IEnumerable<EmailAddress> ownAccountAddresses,
        IEnumerable<SenderDomain> ownAccountDomains,
        IEnumerable<TrustedSenderEntry> configuredTrustedSenders,
        IEnumerable<TrustedSenderEntry> storedTrustedSenders)
    {
        ArgumentNullException.ThrowIfNull(ownAccountAddresses);
        ArgumentNullException.ThrowIfNull(ownAccountDomains);
        ArgumentNullException.ThrowIfNull(configuredTrustedSenders);
        ArgumentNullException.ThrowIfNull(storedTrustedSenders);

        return new SenderTrustPolicy(
            [.. ownAccountAddresses.Select(OwnAddressEntryOf).OfType<TrustedSenderEntry>().Distinct()],
            [.. ownAccountDomains],
            [.. configuredTrustedSenders],
            [.. storedTrustedSenders]);
    }

    /// <summary>Decides what this deployment makes of one message's author.</summary>
    /// <param name="authentication">What the receiving mail server established about the message.</param>
    /// <param name="displayedSender">The address the message's <c>From</c> header displays, where it wrote a usable one.</param>
    /// <param name="organizationVerification">
    /// What verifying the message against the receiving account's organization established, or <see langword="null" />
    /// where the account belongs to no organization and nothing was verified.
    /// </param>
    /// <returns>
    /// The verdict, carrying <see cref="Revision" />, or a revision that also names the receiving organization where
    /// <paramref name="organizationVerification" /> was given, so a verdict says whose keys it was reached under.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authentication" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// <b>Organization membership is asked first, and needs no authenticated domain.</b> A signature of the receiving
    /// account's organization proves the author's address rather than a domain, which is more than the lists below ever
    /// hold, so where it holds the lists are not consulted; where it does not, the message is judged by them exactly as
    /// it would have been with no organization at all.
    /// </para>
    /// <para>
    /// <b>The owner's own addresses are asked before their domains.</b> Where an account opted into the domains, the
    /// owner writing as themselves matches both, and the verdict names the narrower claim.
    /// </para>
    /// <para>
    /// <b>The subject the lists are held against is <see cref="SenderAuthentication.AuthenticatedAuthorDomain" /> and
    /// nothing else.</b> The <c>From</c> header on its own is written by whoever sent the message, so holding it against a list
    /// would let anybody be recognized by claiming to be; and the identity the receiving server happened to authenticate
    /// belongs to whoever handed the message over, which for a relay, a mailing list, or a delivery provider is not the
    /// author at all. Recognizing a provider one correspondent uses would otherwise recognize every message that
    /// provider ever relays, whoever it says wrote them.
    /// </para>
    /// <para>
    /// So a message whose author was not established, and one whose author authentication the receiving server refused,
    /// both reach <see cref="SenderTrustLevel.Unknown" /> without the list being consulted at all, unless organization
    /// membership was proved as stated above. Which of the two it
    /// was stays on <see cref="SenderAuthentication.AuthorAuthentication" />, where it was recorded, because this
    /// verdict answers only about the list.
    /// </para>
    /// </remarks>
    public SenderTrust Evaluate(
        SenderAuthentication authentication,
        EmailAddress? displayedSender,
        OrganizationSenderVerification? organizationVerification = null)
    {
        ArgumentNullException.ThrowIfNull(authentication);

        var revision = organizationVerification is { ReceivingOrganizationId: var organizationId }
            ? SenderTrustPolicyRevision.Of([.. this.statements, $"organization:{organizationId:D}"])
            : this.Revision;

        if (organizationVerification is { SenderIsMember: true })
        {
            return SenderTrust.TrustedAsOrganizationMember(organizationVerification.ReceivingOrganizationId, revision);
        }

        if (authentication.AuthenticatedAuthorDomain is not { } authorDomain)
        {
            return SenderTrust.Unknown(revision);
        }

        if (Recognizes(this.ownAccountAddresses, authorDomain, displayedSender))
        {
            return SenderTrust.Trusted(SenderTrustSource.OwnAccountAddress, revision);
        }

        if (this.ownAccountDomains.Contains(authorDomain))
        {
            return SenderTrust.Trusted(SenderTrustSource.OwnAccountDomain, revision);
        }

        if (Recognizes(this.configuredEntries, authorDomain, displayedSender))
        {
            return SenderTrust.Trusted(SenderTrustSource.ConfiguredTrustedSender, revision);
        }

        return Recognizes(this.storedEntries, authorDomain, displayedSender)
            ? SenderTrust.Trusted(SenderTrustSource.StoredTrustedSender, revision)
            : SenderTrust.Unknown(revision);
    }

    /// <summary>Reads one of the owner's addresses as the address entry it is matched as, or nothing where it names no mailbox.</summary>
    private static TrustedSenderEntry? OwnAddressEntryOf(EmailAddress address) =>
        TrustedSenderEntry.TryCreateForAddress(address.Address, out var entry) ? entry : null;

    /// <summary>Answers whether one set of entries names the author.</summary>
    private static bool Recognizes(
        IReadOnlyList<TrustedSenderEntry> entries,
        SenderDomain authorDomain,
        EmailAddress? displayedSender) =>
        entries.Any(entry => entry.Matches(authorDomain, displayedSender));
}
