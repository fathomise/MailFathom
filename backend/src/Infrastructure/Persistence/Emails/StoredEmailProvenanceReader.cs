// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

[RequiresIntegrationCoverage]
internal sealed class StoredEmailProvenanceReader(
    MailFathomDbContext dbContext,
    IDeploymentMailFolders deploymentFolders) : IEmailProvenanceReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<StoredEmailId, EmailProvenance>> ReadAsync(
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emails);

        if (emails.Count is 0)
        {
            return new Dictionary<StoredEmailId, EmailProvenance>();
        }

        var visibleToAI = await deploymentFolders.ReadAsync(MailFolderSelection.VisibleToAI, cancellationToken);
        var rows = await ProvenanceOf(dbContext, emails, visibleToAI).ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            static row => StoredEmailId.Create(row.StoredEmailId),
            static row => new EmailProvenance
            {
                Sender = new SenderVerification
                {
                    AuthorAuthentication = row.AuthorAuthentication,
                    DeploymentTrust = row.DeploymentTrust,
                    Signature = row.Signature.ToSignature(),
                },
                IsInJunkFolder = row.IsInJunkFolder,
                IsFirstContact = !row.HasEarlierCorrespondence,
            });
    }

    /// <summary>Composes the one query the provenance of a set of emails is read from.</summary>
    /// <param name="context">The scoped context the read runs on.</param>
    /// <param name="emails">The emails whose provenance is wanted.</param>
    /// <param name="visibleToAI">The folders AI may reach, which are the only ones earlier correspondence is looked for in.</param>
    /// <returns>The query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// <para>
    /// Exposed so the generated command can be read by a test: that the earlier correspondence is an existence test
    /// PostgreSQL answers per email, rather than a set of messages carried back here, is invisible in a result.
    /// </para>
    /// <para>
    /// Earlier correspondence is what the contact correspondence index reads as correspondence — the sender wrote the
    /// mail, or it was addressed to them in <c>To</c> or <c>Cc</c> — within the email's own mailbox, outside the folders
    /// contact collection ignores, and received strictly before the email. Strictly, so a copy of the same message in a
    /// second folder is not an earlier one. An earlier message that carries no arrival instant — a copy of the person's
    /// own mail filed in Sent has no <c>Received</c> header — is placed by when it was sent instead. An email whose own
    /// arrival instant is unknown cannot be placed before or after anything, so any other mail of its mailbox counts,
    /// apart from a copy carrying its own <c>Message-ID</c>.
    /// </para>
    /// <para>
    /// The earlier mail is narrowed the way every read a caller is answered from is narrowed: to rows still part of the
    /// mailbox and to the folders a mapping admits to AI. The flag is published to the model and to the caller, so mail
    /// an operator unmapped or a server expunged must not decide it — a sender whose only earlier message is hidden is a
    /// first contact, rather than a hint that the hidden mail exists. The account is already the email's own, which
    /// the caller read.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailProvenanceRow> ProvenanceOf(
        MailFathomDbContext context,
        IReadOnlyCollection<StoredEmailId> emails,
        IReadOnlyList<MailFolderIdentity> visibleToAI)
    {
        var identities = emails.Select(static email => email.Value).ToArray();
        var stored = context.StoredEmails.AsNoTracking();
        var correspondence = AccountScopedMailFolders.Admitting(
            stored.Where(StoredEmailTombstone.IsNotTombstoned).Where(StoredEmailSettlement.IsSettled),
            visibleToAI);

        return stored
            .Where(email => identities.Contains(email.Id))
            .Select(email => new StoredEmailProvenanceRow(
                email.Id,
                email.AuthorAuthenticationOutcome,
                email.SenderTrustLevel,
                new StoredEmailSignatureRow(
                    email.SignatureFormat,
                    email.SignatureSecureMimeType,
                    email.SignatureUnverifiableReason,
                    email.SignatureSignedAt,
                    email.SignatureContentCheck,
                    email.SignatureAuthorityCheck,
                    email.SignatureTrustAnchor,
                    email.SignatureChainEnd,
                    email.SignatureValidityCheck,
                    email.SignatureValidityFailure,
                    email.SignatureRevocationCheck,
                    email.SignatureRevocationMethod,
                    email.SignatureRevocationCheckedAt,
                    email.SignatureRevokedAt,
                    email.SignatureSignerCheck,
                    email.SignerName,
                    email.SignerAddress,
                    email.SignerIssuer,
                    email.SignerValidFrom,
                    email.SignerValidUntil,
                    email.SignerSerialNumber,
                    email.SignerFingerprint,
                    email.SignerKeyId,
                    email.SignerPublicKeyAlgorithm,
                    email.SignerDigestAlgorithm),
                email.Folder.Role == MailFolderSpecialUse.Junk,
                email.SenderNormalizedAddress != null
                    && correspondence.Any(earlier =>
                        earlier.MailboxAccountId == email.MailboxAccountId
                        && earlier.Id != email.Id
                        && earlier.Folder.Role != MailFolderSpecialUse.Junk
                        && earlier.Folder.Role != MailFolderSpecialUse.Trash
                        && earlier.Folder.Role != MailFolderSpecialUse.Drafts
                        && (email.ReceivedAt == null
                            ? email.InternetMessageId == null || earlier.InternetMessageId != email.InternetMessageId
                            : (earlier.ReceivedAt ?? earlier.SentAt) < email.ReceivedAt)
                        && (earlier.SenderNormalizedAddress == email.SenderNormalizedAddress
                            || earlier.ToAddresses.Contains(email.SenderNormalizedAddress)
                            || earlier.CcAddresses.Contains(email.SenderNormalizedAddress)))));
    }
}

/// <summary>What the store recorded about where one email came from.</summary>
/// <param name="StoredEmailId">The email.</param>
/// <param name="AuthorAuthentication">What the receiving server established about the displayed author.</param>
/// <param name="DeploymentTrust">Whether this deployment recognizes that author.</param>
/// <param name="Signature">What verifying the email's own signature found.</param>
/// <param name="IsInJunkFolder">Whether the email sits in its account's junk folder.</param>
/// <param name="HasEarlierCorrespondence">Whether the mailbox held earlier mail written by or addressed to the sender.</param>
internal sealed record StoredEmailProvenanceRow(
    Guid StoredEmailId,
    AuthorAuthenticationOutcome AuthorAuthentication,
    SenderTrustLevel DeploymentTrust,
    StoredEmailSignatureRow Signature,
    bool IsInJunkFolder,
    bool HasEarlierCorrespondence);
