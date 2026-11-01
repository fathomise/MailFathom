// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

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
/// Contacts are not asked here at all. This decides what configuration and the organization's signature answer, which is
/// what a message stores, and the revision digests exactly that — the owner's addresses and domains, whether the account
/// is strict, and the receiving organization. The contacts that hold the author's address are asked whenever the
/// message is read, through <see cref="SenderTrust.RecognizedBy" />, so a contact change never needs stored mail
/// rewritten.
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
    /// account is by default. The contacts found when the message is read still count under it, because they are the
    /// people who wrote them down rather than this account's configuration.
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

    /// <summary>Answers whether an address is one the receiving account's owner holds, compared in its comparison form.</summary>
    /// <param name="address">The address to ask about.</param>
    /// <returns><see langword="true" /> where the owner holds the address on one of their accounts; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// It is the set <see cref="Evaluate" /> recognizes an own address by, asked without any of the evidence a verdict
    /// needs: a reader is never offered to write down an address that is already theirs.
    /// </remarks>
    public bool IsOwnAccountAddress(EmailAddress address) => this.ownAccountAddresses.Contains(address);

    /// <summary>Decides what configuration and the organization's signature make of one message's author, which is the half of the verdict the message stores.</summary>
    /// <param name="authentication">What the receiving mail server, or local verification, established about the message.</param>
    /// <param name="signature">What verifying the message's own S/MIME or OpenPGP signature found.</param>
    /// <param name="displayedSender">The first mailbox of the message's <c>From</c> header, where it wrote a usable one.</param>
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
    /// <b>The sources are asked in order, highest first</b>: organization member, the owner's address, the owner's
    /// domain. Each is asked only where the one above did not recognize the author, so a sender several of them recognize
    /// is reported under the highest. The two contact sources rank between and below them and are asked on read, by
    /// <see cref="SenderTrust.RecognizedBy" />, in that same order.
    /// </para>
    /// <para>
    /// <b>Organization membership needs nothing else.</b> A signature of the receiving account's organization proves the
    /// author's address and the recipient, which is more than anything below establishes.
    /// </para>
    /// <para>
    /// <b>Every other source needs the message tied to the address first</b>, because <c>From</c> on its own is written
    /// by whoever sent the message, and where nothing tied it a relaxed account still recognizes the address as
    /// <see cref="SenderTrustLevel.Known" />. <see cref="SenderAddressRecognition" /> states that rule once for every
    /// source, the contacts asked on read included.
    /// </para>
    /// </remarks>
    public SenderTrust Evaluate(
        SenderAuthentication authentication,
        EmailSignature signature,
        EmailAddress? displayedSender,
        OrganizationSenderVerification? organizationVerification = null)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(signature);

        var revision = organizationVerification is { ReceivingOrganizationId: var organizationId }
            ? SenderTrustPolicyRevision.Of([.. this.statements, $"organization:{organizationId:D}"])
            : this.Revision;

        if (organizationVerification is { SenderIsMember: true })
        {
            return SenderTrust.TrustedAsOrganizationMember(
                organizationVerification.ReceivingOrganizationId,
                revision,
                this.IsStrict);
        }

        if (displayedSender is not { } sender
            || !SenderDomain.TryCreateFromMailbox(sender.Address, out var senderDomain)
            || this.Recognize(sender, senderDomain) is not { } source)
        {
            return SenderTrust.Unknown(revision, this.IsStrict);
        }

        return SenderAddressRecognition.Of(authentication, signature, senderDomain, this.IsStrict)
                .Grant(source, contact: null, revision, this.IsStrict)
            ?? SenderTrust.Unknown(revision, this.IsStrict);
    }

    /// <summary>Names the highest of the owner's own address and domain that holds the displayed address.</summary>
    private SenderTrustSource? Recognize(EmailAddress sender, SenderDomain senderDomain)
    {
        if (this.ownAccountAddresses.Contains(sender))
        {
            return SenderTrustSource.OwnAccountAddress;
        }

        return this.ownAccountDomains.Contains(senderDomain) ? SenderTrustSource.OwnAccountDomain : null;
    }
}
