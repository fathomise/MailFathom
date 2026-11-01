// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What this deployment decided about a message's author.</summary>
/// <remarks>
/// <para>
/// It is the second of a message's two sender verdicts and reads the first. <see cref="SenderAuthentication" /> says
/// what the receiving server established about the message, which is a fact about the message; this says whether the
/// author is one this deployment recognizes, which is a decision about the contacts people and administrators wrote
/// down, and whether that recognition rests on an authenticated author or on the <c>From</c> address alone. Keeping
/// them apart is what lets a contact change without the facts changing under it: how the authentication failed stays
/// the other verdict's to state, and this records only what the deployment concluded from it.
/// </para>
/// <para>
/// It is reached in two halves. What configuration and the organization's signature answer — membership, the owner's own
/// address and domain, and whether the account was strict — is decided when the message is stored and kept with it,
/// under <see cref="PolicyRevision" />, because configuration changes reach stored mail only through a rederive. Which
/// contact holds the address is asked whenever the message is read, of the books as they stand then, and
/// <see cref="RecognizedBy" /> combines the two in the order the sources rank. So a contact written, amended, or erased
/// changes the next read's verdict without anything rewriting stored mail, and <see cref="RecognizingContactId" /> names
/// the contact found at that read. A verdict nothing judged carries <see cref="SenderTrustPolicyRevision.None" />.
/// </para>
/// </remarks>
public sealed record SenderTrust
{
    private SenderTrust(
        SenderTrustLevel level,
        SenderTrustSource grantedBy,
        SenderTrustPolicyRevision policyRevision,
        TrustedAuthorEvidence authorEvidence = TrustedAuthorEvidence.None,
        Guid? recognizingOrganizationId = null,
        ContactId? recognizingContactId = null,
        bool isStrict = false)
    {
        this.Level = level;
        this.GrantedBy = grantedBy;
        this.PolicyRevision = policyRevision;
        this.AuthorEvidence = authorEvidence;
        this.RecognizingOrganizationId = recognizingOrganizationId;
        this.RecognizingContactId = recognizingContactId;
        this.IsStrict = isStrict;
    }

    /// <summary>Gets the verdict a message reaching persistence without a policy having spoken carries.</summary>
    /// <remarks>
    /// Unknown, because that is the answer that claims nothing, and carrying no revision, which is what says a policy
    /// never ran rather than that one ran and recognized nobody. It is what an extraction produces before the
    /// deployment's policy is applied to it, and what a message whose payload was never stored keeps.
    /// </remarks>
    public static SenderTrust NotEvaluated { get; } = new(
        SenderTrustLevel.Unknown,
        SenderTrustSource.None,
        SenderTrustPolicyRevision.None);

    /// <summary>Gets what this deployment decided, which is the value everything above this reads first.</summary>
    public SenderTrustLevel Level { get; }

    /// <summary>Gets which half of what this deployment knows recognized the author, or that none did.</summary>
    public SenderTrustSource GrantedBy { get; }

    /// <summary>Gets the trusted-sender policy the decision was reached under.</summary>
    public SenderTrustPolicyRevision PolicyRevision { get; }

    /// <summary>Gets the organization whose signature recognized the author, present exactly when <see cref="GrantedBy" /> is <see cref="SenderTrustSource.OrganizationMember" />.</summary>
    /// <remarks>
    /// A reference rather than a name, so nothing about the organization is copied onto the message; an organization
    /// deleted since leaves the reason standing without one.
    /// </remarks>
    public Guid? RecognizingOrganizationId { get; }

    /// <summary>Gets the contact that recognized the author, present exactly when <see cref="GrantedBy" /> is <see cref="SenderTrustSource.OrganizationContact" /> or <see cref="SenderTrustSource.PersonalContact" />.</summary>
    /// <remarks>
    /// A reference rather than a copy, so the verdict leads back to the contact and to who wrote its address. It is never
    /// stored: it is the contact the books held when the verdict was read, so one erased since is simply not found again.
    /// </remarks>
    public ContactId? RecognizingContactId { get; }

    /// <summary>Gets whether the receiving account recognized only authors something tied to the address when the message was stored.</summary>
    /// <remarks>
    /// Kept with the stored half because a contact found on a later read may recognize the author by address alone only
    /// where the account was not strict, and <see cref="PolicyRevision" /> digests that statement without anything being
    /// able to read it back.
    /// </remarks>
    public bool IsStrict { get; }

    /// <summary>Gets what tied the message to the address or domain the source recognized.</summary>
    /// <remarks>
    /// <see cref="TrustedAuthorEvidence.AuthorSignature" /> only where authentication did not hold, so the record names
    /// what the mark rested on; <see cref="TrustedAuthorEvidence.None" /> for every verdict that is not trusted and for
    /// one the organization's signature reached.
    /// </remarks>
    public TrustedAuthorEvidence AuthorEvidence { get; }

    /// <summary>Gets whether this deployment recognized the message's author and something tied the message to them, which <see cref="SenderTrustLevel.Trusted" /> alone is; a <see cref="SenderTrustLevel.Known" /> verdict is not.</summary>
    public bool IsTrusted => this.Level == SenderTrustLevel.Trusted;

    /// <summary>Records that this deployment recognizes nobody in the message.</summary>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <param name="isStrict">Whether the account was strict.</param>
    /// <returns>The verdict.</returns>
    /// <remarks>
    /// It covers every way of arriving there: no author was authenticated, the author's authentication failed, or an
    /// author authenticated and nothing this deployment knows names them. Those differ in what the receiving server
    /// established rather than in what this deployment decided, so they stay on <see cref="SenderAuthentication" />
    /// instead of being folded in here.
    /// </remarks>
    public static SenderTrust Unknown(SenderTrustPolicyRevision policyRevision, bool isStrict = false) =>
        new(SenderTrustLevel.Unknown, SenderTrustSource.None, policyRevision, isStrict: isStrict);

    /// <summary>Records that the author wrote as one of the receiving account owner's own addresses or domains.</summary>
    /// <param name="grantedBy"><see cref="SenderTrustSource.OwnAccountAddress" /> or <see cref="SenderTrustSource.OwnAccountDomain" />.</param>
    /// <param name="authorEvidence">What tied the message to that address or domain.</param>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <param name="isStrict">Whether the account was strict.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when another source is named, each of which <see cref="TrustedAsOrganizationMember" /> or
    /// <see cref="TrustedByContact" /> records with the reference it needs, or when no evidence is named.
    /// </exception>
    public static SenderTrust TrustedAsOwn(
        SenderTrustSource grantedBy,
        TrustedAuthorEvidence authorEvidence,
        SenderTrustPolicyRevision policyRevision,
        bool isStrict = false)
    {
        if (grantedBy is not (SenderTrustSource.OwnAccountAddress or SenderTrustSource.OwnAccountDomain))
        {
            throw new ArgumentException(
                "Only the owner's own address or domain is recorded without a reference.",
                nameof(grantedBy));
        }

        return new SenderTrust(
            SenderTrustLevel.Trusted,
            grantedBy,
            policyRevision,
            RequireEvidence(authorEvidence),
            isStrict: isStrict);
    }

    /// <summary>Records that a contact somebody wrote down holds the author's address.</summary>
    /// <param name="grantedBy"><see cref="SenderTrustSource.OrganizationContact" /> or <see cref="SenderTrustSource.PersonalContact" />.</param>
    /// <param name="contactId">The contact that recognized the author.</param>
    /// <param name="authorEvidence">What tied the message to the contact's address.</param>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <param name="isStrict">Whether the account was strict.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentException">Thrown when another source is named, or when no evidence is named.</exception>
    public static SenderTrust TrustedByContact(
        SenderTrustSource grantedBy,
        ContactId contactId,
        TrustedAuthorEvidence authorEvidence,
        SenderTrustPolicyRevision policyRevision,
        bool isStrict = false)
    {
        if (grantedBy is not (SenderTrustSource.OrganizationContact or SenderTrustSource.PersonalContact))
        {
            throw new ArgumentException("Only a contact source names a contact.", nameof(grantedBy));
        }

        return new SenderTrust(
            SenderTrustLevel.Trusted,
            grantedBy,
            policyRevision,
            RequireEvidence(authorEvidence),
            recognizingContactId: contactId,
            isStrict: isStrict);
    }

    /// <summary>Records that a source recognizes the address the message displays, while nothing tied the message to it.</summary>
    /// <param name="grantedBy">Any source but <see cref="SenderTrustSource.OrganizationMember" />, which proves the address itself and is never this weaker claim.</param>
    /// <param name="contactId">The contact that recognized the address, present exactly when <paramref name="grantedBy" /> is a contact source.</param>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <returns>The verdict, carrying no evidence because none tied the message to the address.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when no source or the organization signature is named, or when a contact is named for a source that is not
    /// a contact or missing for one that is.
    /// </exception>
    public static SenderTrust Known(
        SenderTrustSource grantedBy,
        ContactId? contactId,
        SenderTrustPolicyRevision policyRevision)
    {
        var namesAContact = grantedBy is SenderTrustSource.OrganizationContact or SenderTrustSource.PersonalContact;

        if (grantedBy is SenderTrustSource.None or SenderTrustSource.OrganizationMember
            || namesAContact != contactId.HasValue)
        {
            throw new ArgumentException(
                "A known sender is named by a contact, the owner's address, or the owner's domain, and a contact source names its contact.",
                nameof(grantedBy));
        }

        return new SenderTrust(
            SenderTrustLevel.Known,
            grantedBy,
            policyRevision,
            recognizingContactId: contactId);
    }

    /// <summary>Records that a signature of the receiving account's organization proved the author a member of it.</summary>
    /// <param name="organizationId">The organization whose key verified the signature.</param>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <param name="isStrict">Whether the account was strict.</param>
    /// <returns>The verdict.</returns>
    public static SenderTrust TrustedAsOrganizationMember(
        Guid organizationId,
        SenderTrustPolicyRevision policyRevision,
        bool isStrict = false) =>
        new(
            SenderTrustLevel.Trusted,
            SenderTrustSource.OrganizationMember,
            policyRevision,
            recognizingOrganizationId: organizationId,
            isStrict: isStrict);

    /// <summary>Reads back the half of a verdict a message stores: what configuration and the organization's signature answered.</summary>
    /// <param name="level">The stored level.</param>
    /// <param name="grantedBy">The stored source, which is never a contact.</param>
    /// <param name="authorEvidence">What tied the message to the source's address or domain.</param>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <param name="recognizingOrganizationId">The organization whose signature recognized the author, where one did.</param>
    /// <param name="isStrict">Whether the account was strict.</param>
    /// <returns>The verdict as it was stored.</returns>
    /// <remarks>
    /// Nothing re-derives the stored half here, so nothing re-checks it either: the row is what an extraction wrote, and
    /// <see cref="RecognizedBy" /> is what turns it into the verdict a reader sees.
    /// </remarks>
    public static SenderTrust FromStored(
        SenderTrustLevel level,
        SenderTrustSource grantedBy,
        TrustedAuthorEvidence authorEvidence,
        SenderTrustPolicyRevision policyRevision,
        Guid? recognizingOrganizationId,
        bool isStrict) =>
        new(level, grantedBy, policyRevision, authorEvidence, recognizingOrganizationId, isStrict: isStrict);

    /// <summary>Combines what the message stores with the contacts that hold its author's address when it is read.</summary>
    /// <param name="contacts">The trust-granting contacts holding the first <c>From</c> mailbox in the books the receiving account reaches now.</param>
    /// <param name="recognition">What recognizing that address grants the message.</param>
    /// <returns>The verdict a reader sees.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The sources rank as they do when a message is stored: organization member, organization contact, the owner's
    /// address, the owner's domain, a personal contact. Membership and the owner's own address and domain are the stored
    /// half's answer; the two contact sources are asked here, each only where every source above it answered nothing.
    /// A contact recognizes the author under the same rule the owner's address does, so a contact that makes nothing of a
    /// message leaves the stored answer standing.
    /// </remarks>
    public SenderTrust RecognizedBy(RecognizingContacts contacts, SenderAddressRecognition recognition)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(recognition);

        if (this.GrantedBy == SenderTrustSource.OrganizationMember)
        {
            return this;
        }

        if (contacts.OrganizationContact is { } organizationContact)
        {
            return recognition.Grant(SenderTrustSource.OrganizationContact, organizationContact, this.PolicyRevision, this.IsStrict)
                ?? this;
        }

        if (this.GrantedBy != SenderTrustSource.None || contacts.PersonalContact is not { } personalContact)
        {
            return this;
        }

        return recognition.Grant(SenderTrustSource.PersonalContact, personalContact, this.PolicyRevision, this.IsStrict)
            ?? this;
    }

    private static TrustedAuthorEvidence RequireEvidence(TrustedAuthorEvidence authorEvidence) =>
        authorEvidence != TrustedAuthorEvidence.None
            ? authorEvidence
            : throw new ArgumentException(
                "A source somebody wrote down counts only for a message tied to it.",
                nameof(authorEvidence));
}
