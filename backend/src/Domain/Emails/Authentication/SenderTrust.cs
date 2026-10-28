// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What this deployment decided about the author one message authenticated as.</summary>
/// <remarks>
/// <para>
/// It is the second of a message's two sender verdicts and reads the first. <see cref="SenderAuthentication" /> says
/// what the receiving server established about the message, which is a fact about the message; this says whether the
/// author is one this deployment recognizes, which is a decision about the contacts people and administrators wrote
/// down. Keeping them apart is what lets a contact change without the facts changing under it, and it is why this
/// carries no value for <em>the author was not authenticated</em> — that is the other verdict's to state.
/// </para>
/// <para>
/// It is stored rather than computed on read, so a message keeps the answer it was judged with. What makes that
/// legible is <see cref="PolicyRevision" />, which names the configuration the answer was reached under, and
/// <see cref="RecognizingContactId" />, which names the contact that recognized the author. A verdict nothing judged
/// carries <see cref="SenderTrustPolicyRevision.None" />.
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
        ContactId? recognizingContactId = null)
    {
        this.Level = level;
        this.GrantedBy = grantedBy;
        this.PolicyRevision = policyRevision;
        this.AuthorEvidence = authorEvidence;
        this.RecognizingOrganizationId = recognizingOrganizationId;
        this.RecognizingContactId = recognizingContactId;
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
    /// A reference rather than a copy, so the verdict leads back to the contact and to who wrote its address. Unlike the
    /// organization's reference it is never cleared on its own: every act that removes a contact resets each verdict
    /// naming it to <see cref="NotEvaluated" /> in its own transaction, so no trusted or known mark is left naming nothing.
    /// </remarks>
    public ContactId? RecognizingContactId { get; }

    /// <summary>Gets what tied the message to the address or domain the source recognized.</summary>
    /// <remarks>
    /// <see cref="TrustedAuthorEvidence.AuthorSignature" /> only where authentication did not hold, so the record names
    /// what the mark rested on; <see cref="TrustedAuthorEvidence.None" /> for every verdict that is not trusted and for
    /// one the organization's signature reached.
    /// </remarks>
    public TrustedAuthorEvidence AuthorEvidence { get; }

    /// <summary>Gets whether this deployment recognized the message's author.</summary>
    public bool IsTrusted => this.Level == SenderTrustLevel.Trusted;

    /// <summary>Records that this deployment recognizes nobody in the message.</summary>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <returns>The verdict.</returns>
    /// <remarks>
    /// It covers every way of arriving there: no author was authenticated, the author's authentication failed, or an
    /// author authenticated and nothing this deployment knows names them. Those differ in what the receiving server
    /// established rather than in what this deployment decided, so they stay on <see cref="SenderAuthentication" />
    /// instead of being folded in here.
    /// </remarks>
    public static SenderTrust Unknown(SenderTrustPolicyRevision policyRevision) =>
        new(SenderTrustLevel.Unknown, SenderTrustSource.None, policyRevision);

    /// <summary>Records that the author wrote as one of the receiving account owner's own addresses or domains.</summary>
    /// <param name="grantedBy"><see cref="SenderTrustSource.OwnAccountAddress" /> or <see cref="SenderTrustSource.OwnAccountDomain" />.</param>
    /// <param name="authorEvidence">What tied the message to that address or domain.</param>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when another source is named, each of which <see cref="TrustedAsOrganizationMember" /> or
    /// <see cref="TrustedByContact" /> records with the reference it needs, or when no evidence is named.
    /// </exception>
    public static SenderTrust TrustedAsOwn(
        SenderTrustSource grantedBy,
        TrustedAuthorEvidence authorEvidence,
        SenderTrustPolicyRevision policyRevision)
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
            RequireEvidence(authorEvidence));
    }

    /// <summary>Records that a contact somebody wrote down holds the author's address.</summary>
    /// <param name="grantedBy"><see cref="SenderTrustSource.OrganizationContact" /> or <see cref="SenderTrustSource.PersonalContact" />.</param>
    /// <param name="contactId">The contact that recognized the author.</param>
    /// <param name="authorEvidence">What tied the message to the contact's address.</param>
    /// <param name="policyRevision">The policy that was applied.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentException">Thrown when another source is named, or when no evidence is named.</exception>
    public static SenderTrust TrustedByContact(
        SenderTrustSource grantedBy,
        ContactId contactId,
        TrustedAuthorEvidence authorEvidence,
        SenderTrustPolicyRevision policyRevision)
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
            recognizingContactId: contactId);
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
    /// <returns>The verdict.</returns>
    public static SenderTrust TrustedAsOrganizationMember(
        Guid organizationId,
        SenderTrustPolicyRevision policyRevision) =>
        new(
            SenderTrustLevel.Trusted,
            SenderTrustSource.OrganizationMember,
            policyRevision,
            recognizingOrganizationId: organizationId);

    private static TrustedAuthorEvidence RequireEvidence(TrustedAuthorEvidence authorEvidence) =>
        authorEvidence != TrustedAuthorEvidence.None
            ? authorEvidence
            : throw new ArgumentException(
                "A source somebody wrote down counts only for a message tied to it.",
                nameof(authorEvidence));
}
