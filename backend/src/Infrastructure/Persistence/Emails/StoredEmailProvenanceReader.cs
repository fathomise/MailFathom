// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Contacts;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

[RequiresIntegrationCoverage]
internal sealed class StoredEmailProvenanceReader(
    MailFathomDbContext dbContext,
    IDeploymentMailFolders deploymentFolders,
    TimeProvider timeProvider) : IEmailProvenanceReader
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
        var verdicts = await SenderTrustBooks.RecognizeAsync(
            dbContext,
            [.. rows.Select(static row => row.SenderTrust)],
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var unsent = (await UnsentOf(dbContext, emails).ToArrayAsync(cancellationToken))
            .ToDictionary(static row => row.StoredEmailId, row => StateOf(row.Stage, row.DueAt, now));

        return rows.Zip(verdicts).ToDictionary(
            static pair => StoredEmailId.Create(pair.First.StoredEmailId),
            pair => new EmailProvenance
            {
                Sender = new SenderVerification
                {
                    AuthorAuthentication = pair.First.SenderTrust.AuthorAuthentication,
                    DeploymentTrust = pair.Second.Level,
                    Signature = pair.First.Signature.ToSignature(),
                },
                IsInJunkFolder = pair.First.IsInJunkFolder,
                IsFirstContact = !pair.First.HasEarlierCorrespondence,
                IsWrittenByMailbox = pair.First.IsWrittenByMailbox,
                Unsent = unsent.TryGetValue(pair.First.StoredEmailId, out var state) ? state : null,
            });
    }

    /// <summary>Reads where a message the mailbox has not sent stands, from the stage of the send its draft was promoted to.</summary>
    /// <param name="stage">The stage of that send, or <see langword="null" /> where the draft was promoted to none.</param>
    /// <param name="dueAt">The instant that send was written to leave at, or <see langword="null" /> where it was written to leave at once.</param>
    /// <param name="now">The instant the read happens at.</param>
    /// <returns>The state the person's own Agent is told.</returns>
    /// <remarks>
    /// A send already delivered still has its copy in the drafts folder until the draft pass removes it, and it is read as
    /// still leaving rather than as delivered, because the copy shown is not the one that left. A withdrawn send is a
    /// working copy again, which nobody is asking to send.
    /// </remarks>
    internal static UnsentEmailState StateOf(OutgoingEmailStage? stage, DateTimeOffset? dueAt, DateTimeOffset now) => stage switch
    {
        null or OutgoingEmailStage.Cancelled => UnsentEmailState.Draft,
        OutgoingEmailStage.Recorded when dueAt > now => UnsentEmailState.Scheduled,
        OutgoingEmailStage.Refused => UnsentEmailState.Failed,
        _ => UnsentEmailState.Sending,
    };

    /// <summary>Composes the query that reads, for those of a set of emails the mailbox has not sent, the send each one's draft was promoted to.</summary>
    /// <param name="context">The scoped context the read runs on.</param>
    /// <param name="emails">The emails whose provenance is wanted.</param>
    /// <returns>One row per unsent email, naming the stage and the due instant of its send where its draft was promoted to one.</returns>
    /// <remarks>
    /// A draft names the stored copy filed for it and the send it was promoted to, so the copy reaches its send through
    /// the draft. A draft another client wrote names no copy of ours and no send, and reads as a draft.
    /// </remarks>
    internal static IQueryable<StoredUnsentEmailRow> UnsentOf(
        MailFathomDbContext context,
        IReadOnlyCollection<StoredEmailId> emails)
    {
        var identities = emails.Select(static email => email.Value).ToArray();
        var sends = context.OutgoingEmails.AsNoTracking();
        var drafts = context.MailDrafts.AsNoTracking();

        return context.StoredEmails
            .AsNoTracking()
            .Where(email => identities.Contains(email.Id))
            .Where(UnsentStoredEmails.IsUnsent)
            .Select(email => new StoredUnsentEmailRow(
                email.Id,
                sends
                    .Where(send => drafts.Any(draft => draft.FiledStoredEmailId == email.Id && draft.PromotedToOutgoingEmailId == send.Id))
                    .Select(send => (OutgoingEmailStage?)send.Stage)
                    .FirstOrDefault(),
                sends
                    .Where(send => drafts.Any(draft => draft.FiledStoredEmailId == email.Id && draft.PromotedToOutgoingEmailId == send.Id))
                    .Select(send => send.DueAt)
                    .FirstOrDefault()));
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
    /// <para>
    /// The mailbox wrote an email that sits in its account's sent folder, or whose <c>From</c> address is the comparison
    /// address the account's own record holds — provided either the email reached no receiving server, which leaves its
    /// arrival instant unknown and is how a copy the person's client filed elsewhere looks, or the receiving server
    /// authenticated that author. A received message that only displays the mailbox's address is neither, so a forged
    /// one keeps every qualification. The record is reached by its identifier written as the account identifier is, and
    /// an account declared only in configuration has no record, so for it the sent folder is the one signal. Aliases
    /// and sending addresses live in the record's document rather than in a column a query can compare, so a message
    /// written under one of them and filed outside the sent folder is read as received.
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
                new StoredSenderTrustRow(
                    email.MailboxAccountId,
                    email.SenderTrustAddress,
                    email.SenderTrustLevel,
                    email.SenderTrustGrantedBy,
                    email.SenderTrustAuthorEvidence,
                    email.SenderTrustPolicyRevision,
                    email.SenderTrustOrganizationId,
                    email.SenderTrustIsStrict,
                    email.AuthorAuthenticationOutcome,
                    email.AuthorAuthenticationRoute,
                    email.AuthenticatedAuthorDomain,
                    email.DmarcPolicy,
                    email.SignatureContentCheck,
                    email.SignatureUnverifiableReason,
                    email.SignatureAuthorityCheck,
                    email.SignatureValidityCheck,
                    email.SignatureRevocationCheck,
                    email.SignatureSignerCheck),
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
                email.Folder.Role == MailFolderSpecialUse.Sent
                    || (email.SenderTrustAddress != null
                        && (email.ReceivedAt == null
                            || email.AuthorAuthenticationOutcome == AuthorAuthenticationOutcome.Authenticated)
                        && context.MailAccountRecords.Any(record =>
                            record.Id.ToString() == email.MailboxAccountId
                            && record.NormalizedEmailAddress == email.SenderTrustAddress)),
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
/// <param name="SenderTrust">The stored half of the trust verdict, beside what the receiving server established about the displayed author.</param>
/// <param name="Signature">What verifying the email's own signature found.</param>
/// <param name="IsInJunkFolder">Whether the email sits in its account's junk folder.</param>
/// <param name="IsWrittenByMailbox">Whether the email sits in its account's sent folder or carries the account's own address as its author.</param>
/// <param name="HasEarlierCorrespondence">Whether the mailbox held earlier mail written by or addressed to the sender.</param>
internal sealed record StoredEmailProvenanceRow(
    Guid StoredEmailId,
    StoredSenderTrustRow SenderTrust,
    StoredEmailSignatureRow Signature,
    bool IsInJunkFolder,
    bool IsWrittenByMailbox,
    bool HasEarlierCorrespondence);

/// <summary>What the store recorded about where one email the mailbox has not sent stands.</summary>
/// <param name="StoredEmailId">The email.</param>
/// <param name="Stage">The stage of the send its draft was promoted to, or <see langword="null" /> where it was promoted to none.</param>
/// <param name="DueAt">The instant that send was written to leave at, or <see langword="null" /> where it was written to leave at once or there is no send.</param>
internal sealed record StoredUnsentEmailRow(Guid StoredEmailId, OutgoingEmailStage? Stage, DateTimeOffset? DueAt);
