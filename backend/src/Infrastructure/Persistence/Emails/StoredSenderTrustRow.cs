// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Signatures;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The columns a stored message keeps of its trust verdict, and of what a contact found when it is read is held against.</summary>
/// <param name="MailboxAccountId">The receiving account, whose books are the ones read.</param>
/// <param name="Address">The first <c>From</c> mailbox in its comparison form, or <see langword="null" /> where the row keeps none and no contact is asked.</param>
/// <param name="Level">The stored level.</param>
/// <param name="GrantedBy">The stored source, which is never a contact.</param>
/// <param name="AuthorEvidence">What tied the message to the stored source's address or domain.</param>
/// <param name="PolicyRevision">The stored revision, or <see langword="null" /> where no policy judged the row.</param>
/// <param name="OrganizationId">The organization whose signature recognized the author, where one did.</param>
/// <param name="IsStrict">Whether the account was strict when the row was judged.</param>
/// <param name="AuthorAuthentication">Whether the author was established, refused, or neither.</param>
/// <param name="AuthorRoute">What established an authenticated author.</param>
/// <param name="AuthenticatedAuthorDomain">The domain the author was authenticated for, where one was.</param>
/// <param name="DmarcPolicy">The policy the author's domain publishes, as read when the row was stored.</param>
/// <param name="SignatureContentCheck">Whether the message's own signature matched its content.</param>
/// <param name="SignatureUnverifiableReason">Why the signature could not be verified, where it could not.</param>
/// <param name="SignatureAuthorityCheck">Whether the signer chained to a trusted authority.</param>
/// <param name="SignatureValidityCheck">Whether the signer was valid when the message was signed.</param>
/// <param name="SignatureRevocationCheck">Whether the signer was not revoked.</param>
/// <param name="SignatureSignerCheck">Whether the signer names the displayed author.</param>
internal sealed record StoredSenderTrustRow(
    string MailboxAccountId,
    string? Address,
    SenderTrustLevel Level,
    SenderTrustSource GrantedBy,
    TrustedAuthorEvidence AuthorEvidence,
    string? PolicyRevision,
    Guid? OrganizationId,
    bool IsStrict,
    AuthorAuthenticationOutcome AuthorAuthentication,
    AuthorAuthenticationRoute AuthorRoute,
    string? AuthenticatedAuthorDomain,
    DmarcPolicyReading DmarcPolicy,
    SignatureCheckResult SignatureContentCheck,
    EmailSignatureUnverifiableReason? SignatureUnverifiableReason,
    SignatureCheckResult SignatureAuthorityCheck,
    SignatureCheckResult SignatureValidityCheck,
    SignatureCheckResult SignatureRevocationCheck,
    SignatureCheckResult SignatureSignerCheck)
{
    /// <summary>Combines the stored half of the verdict with the contacts that hold the address in the account's books now.</summary>
    /// <param name="contacts">The trust-granting contacts the books hold for <see cref="Address" />.</param>
    /// <returns>The verdict a reader sees.</returns>
    /// <remarks>
    /// The signature is rebuilt from the checks alone, which are all its outcome and its author match are decided from;
    /// nothing else of it is read here.
    /// </remarks>
    internal SenderTrust Recognize(RecognizingContacts contacts)
    {
        var stored = SenderTrust.FromStored(
            this.Level,
            this.GrantedBy,
            this.AuthorEvidence,
            SenderTrustPolicyRevision.FromStoredValue(this.PolicyRevision),
            this.OrganizationId,
            this.IsStrict);

        if (contacts == RecognizingContacts.None
            || !SenderDomain.TryCreateFromMailbox(this.Address, out var senderDomain))
        {
            return stored;
        }

        var signature = new EmailSignature
        {
            ContentCheck = this.SignatureContentCheck,
            UnverifiableReason = this.SignatureUnverifiableReason,
            AuthorityCheck = this.SignatureAuthorityCheck,
            ValidityCheck = this.SignatureValidityCheck,
            RevocationCheck = this.SignatureRevocationCheck,
            SignerCheck = this.SignatureSignerCheck,
        };

        return stored.RecognizedBy(
            contacts,
            SenderAddressRecognition.Of(
                this.AuthorAuthentication,
                this.AuthorRoute,
                SenderDomain.TryCreate(this.AuthenticatedAuthorDomain, out var authorDomain) ? authorDomain : null,
                this.DmarcPolicy,
                signature,
                senderDomain,
                this.IsStrict));
    }
}
