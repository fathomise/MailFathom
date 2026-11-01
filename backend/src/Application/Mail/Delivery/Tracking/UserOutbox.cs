// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Localization;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>Serves one user the outbox of each mail account they are assigned: what it holds, one send in it, and the decisions about one.</summary>
/// <remarks>
/// <para>
/// Each account has one outbox, beside its folders rather than one of them: every send that has not left, and every
/// refused send nobody has dismissed.
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0040-each-account-outbox-as-a-read-only-list-beside-its-folders.md">ADR 0040</see>
/// decides it. It is read-only by construction — an entry is named by its <see cref="OutgoingEmailId" />, which no
/// mailbox act takes — so nothing here refuses an act; the only decisions are the three below.
/// </para>
/// <para>
/// <b>The outbox is read under both <see cref="MailFathomPermission.MailRead" /> and
/// <see cref="MailFathomPermission.MailSend" />.</b> It is the not-yet-sent half of the Sent folder: who each send is
/// for and what it is about is correspondence, so a caller entitled to read the account's mail and to send as it
/// learns nothing it could not, and a send-only credential — a notifier, an agent that writes and never reads — reaches
/// no outbox. Such a credential keeps reading one send by identifier through <see cref="FindAsync" />, status only,
/// and keeps the three decisions, which are part of sending.
/// </para>
/// <para>
/// <b>A reading names an account and never the deployment.</b> It is resolved against the accounts the caller's user is
/// assigned, so an account another user is assigned is refused exactly as one nobody configured, and a send from one
/// answers exactly as one nobody made. Whoever asked for a send — the person, an agent, or a rule — is no part of the
/// scoping, because the mail leaves a mailbox this user is assigned and everybody assigned it may see what goes out.
/// </para>
/// <para>
/// It is transport-agnostic on purpose: the client endpoint and the MCP tools call the same methods, so the grant rule
/// and the scoping hold however the outbox is reached.
/// </para>
/// </remarks>
/// <param name="accountCatalog">Says which accounts the caller's user is assigned, and therefore which outbox a reading may name.</param>
/// <param name="outgoingEmails">Holds the durable record of every send.</param>
/// <param name="outbox">Reads an account's outbox and performs the conditional decisions, in the one place each is decided.</param>
/// <param name="contentStore">Holds the stored message a send will transmit.</param>
/// <param name="renderer">Renders that message the way a stored message is read.</param>
/// <param name="textReader">Reads the subject out of a message whose record an earlier build wrote without one.</param>
/// <param name="egressGuard">Scans what an author wrote before it is published.</param>
/// <param name="readOptions">Bounds how much of a message one preview renders.</param>
/// <param name="languages">States a failure in the reader's language.</param>
/// <param name="authorization">Answers which grants the caller that reached this holds.</param>
/// <param name="clientSignals">Tells an open client that a decision moved a send in its account's outbox.</param>
/// <param name="timeProvider">Says whether an attempt holds a send now.</param>
public sealed class UserOutbox(
    ICallerMailAccountCatalog accountCatalog,
    IOutgoingEmailStore outgoingEmails,
    IOutboxOperationStore outbox,
    IEmailContentStore contentStore,
    IEmailContentRenderer renderer,
    IOutgoingMailTextReader textReader,
    SensitiveContentEgressGuard egressGuard,
    EmailContentReadOptions readOptions,
    IUserLanguages languages,
    AccessAuthorization authorization,
    ClientSignals clientSignals,
    TimeProvider timeProvider)
{
    /// <summary>Gets whether the caller holds both grants an outbox is read under, so a surface can leave the outbox out rather than refuse.</summary>
    public bool MayReadOutbox =>
        authorization.Permits(MailFathomPermission.MailRead) && authorization.Permits(MailFathomPermission.MailSend);

    /// <summary>Reads one page of the named account's outbox, newest first.</summary>
    /// <param name="account">The account whose outbox to read, which is required rather than optional.</param>
    /// <param name="stage">The stage among <see cref="OutgoingEmailRecord.OutboxStages" /> to narrow to, or <see langword="null" /> for all three.</param>
    /// <param name="pageSize">How many sends the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, or the reason the request named none.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold both <see cref="MailFathomPermission.MailRead" /> and <see cref="MailFathomPermission.MailSend" />, or is acting for no user.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when <paramref name="account" /> names an account the caller's user is not assigned.</exception>
    /// <remarks>
    /// A stage the outbox does not hold — a delivered or a withdrawn send — is refused as unknown rather than answered
    /// with an empty page, because it is a mistake in the request rather than an outbox that happens to be empty.
    /// </remarks>
    public async Task<UserOutboxPageResult> ReadPageAsync(
        MailAccountSelector account,
        OutgoingEmailStage? stage,
        int? pageSize,
        OutboxCursor? cursor,
        CancellationToken cancellationToken)
    {
        this.RequireOutboxReading();

        var owned = await this.ResolveAssignedAsync(account, cancellationToken);

        if (stage is { } named && !OutgoingEmailRecord.OutboxStages.Contains(named))
        {
            return new UserOutboxPageResult(null, OutboxQueryOutcome.StageUnknown);
        }

        var queryResult = OutboxQuery.Create(owned, stage, pageSize, cursor);

        if (queryResult.Query is not { } query)
        {
            return new UserOutboxPageResult(null, queryResult.Outcome);
        }

        var page = await outbox.ReadAccountOutboxAsync(query, cancellationToken);
        var user = authorization.RequireUser();
        var completed = new List<AccountOutboxEntry>(page.Sends.Count);

        foreach (var entry in page.Sends)
        {
            completed.Add(entry with
            {
                Subject = entry.Subject ?? await this.ReadSubjectFromMessageAsync(entry.OutgoingEmailId, cancellationToken),
                FailureStatement = this.StateFailure(entry.LastFailure, user),
            });
        }

        return new UserOutboxPageResult(
            page with { Sends = await this.GuardedAsync(owned, completed, cancellationToken) },
            OutboxQueryOutcome.Accepted);
    }

    /// <summary>Counts what the outbox of each named account holds, for the accounts among them this user is assigned.</summary>
    /// <param name="accounts">The accounts to count, typically every account a surface is listing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One count per named account this user is assigned; an account they are not is left out rather than refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold both grants an outbox is read under.</exception>
    /// <remarks>
    /// A surface that lists accounts for a caller without both grants asks <see cref="MayReadOutbox" /> first and leaves
    /// the count out, so a credential that may not read the outbox is not told its size either.
    /// </remarks>
    public async Task<IReadOnlyList<AccountOutboxCount>> CountAsync(
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        this.RequireOutboxReading();

        var assigned = (await accountCatalog.ReadAssignedAccountsAsync(cancellationToken))
            .Select(candidate => candidate.Id)
            .ToHashSet();

        return await outbox.CountAccountOutboxesAsync(
            [.. accounts.Where(assigned.Contains).Distinct()],
            cancellationToken);
    }

    /// <summary>Reads back one send of an account this user is assigned, with what each of its recipients was told.</summary>
    /// <param name="outgoingEmailId">The record to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" /> when this user has no send under that identifier.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no user.</exception>
    /// <remarks>
    /// It is what a send-only credential reads: the stage, the attempts, and each recipient's outcome, never the subject
    /// or the message. A caller holding both grants reads <see cref="ReadPreviewAsync" /> instead.
    /// </remarks>
    public async Task<OutgoingEmailRecord?> FindAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        return await this.FindAssignedAsync(outgoingEmailId, cancellationToken);
    }

    /// <summary>Reads one send of an account this user is assigned as the message it is.</summary>
    /// <param name="outgoingEmailId">The send to read.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The preview, or <see langword="null" /> when this user has no send under that identifier.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold both grants an outbox is read under, or is acting for no user.</exception>
    /// <remarks>
    /// Any send of an assigned account is previewed, whatever stage it stands at and whether or not it is still in the
    /// outbox, because the identifier is the one the send answered with and a caller holding it asked about that send.
    /// </remarks>
    public async Task<OutboxPreview?> ReadPreviewAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        this.RequireOutboxReading();

        var user = authorization.RequireUser();

        if (await this.FindAssignedAsync(outgoingEmailId, cancellationToken) is not { } record)
        {
            return null;
        }

        var rendering = await this.RenderAsync(record.Id, cancellationToken);
        var preview = new OutboxPreview
        {
            Send = record,
            IsHeldByAttempt = record.IsHeldByAttemptAt(timeProvider.GetUtcNow()),
            Subject = record.Subject ?? rendering?.Headers.Subject,
            Sender = rendering?.Headers.Participants.FirstOrDefault(participant => participant.Role == EmailAddressRole.From),
            Body = rendering is null ? null : BodyOf(rendering),
            Attachments = rendering?.Attachments ?? [],
            FailureStatement = this.StateFailure(record.LastFailure, user),
            Receipts = await outbox.ReadReceiptsAsync(record.Id, cancellationToken),
        };

        return await this.GuardedAsync(preview, cancellationToken);
    }

    /// <summary>Withdraws one of this user's sends, where nothing has begun transmitting it.</summary>
    /// <param name="outgoingEmailId">The record to withdraw.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the send, which is <see cref="OutboxDecisionOutcome.RecordUnknown" /> for one this user does not hold.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no user.</exception>
    /// <remarks>
    /// The scoping runs before the withdrawal rather than beside it, so a record this user may not be told about is
    /// never named to a store that acts on whatever identifier it is handed. What decides whether the send could still
    /// be withdrawn is the store's own conditional statement, which is what keeps a delivery pass from having a message
    /// cancelled out from under it mid-envelope.
    /// </remarks>
    public async Task<OutboxDecisionOutcome> CancelAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        return await this.FindAssignedAsync(outgoingEmailId, cancellationToken) is { } record
            ? this.Signalled(record, await outbox.CancelAsync(outgoingEmailId, cancellationToken))
            : OutboxDecisionOutcome.RecordUnknown;
    }

    /// <summary>Offers one of this user's sends again, which is the decision this system will not take on its own.</summary>
    /// <param name="outgoingEmailId">The record to offer again.</param>
    /// <param name="refusalRestated">Whether the caller has restated a permanent refusal, which a refused send needs before it is offered again.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the send, which is <see cref="OutboxDecisionOutcome.RecordUnknown" /> for one this user does not hold.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no user.</exception>
    /// <remarks>
    /// <para>
    /// It names one send and never a set, for the reason the administrative decision does: a message whose outcome
    /// nobody knows may already be in somebody's mailbox, so offering it again is a decision about that one message
    /// and a filtered re-queue would be an unknown number of duplicates asked for in one request.
    /// </para>
    /// <para>
    /// A dismissed send is refused as one the decision no longer applies to. Dismissing it took it out of every outbox a
    /// person reads, so offering it again is the operator's, from the administrative surface. The read answers the
    /// ordinary case before a refusal is asked to be restated; the store's statement is what holds against a dismissal
    /// committed after that read.
    /// </para>
    /// </remarks>
    public async Task<OutboxDecisionOutcome> RequeueAsync(
        OutgoingEmailId outgoingEmailId,
        bool refusalRestated,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        return await this.FindAssignedAsync(outgoingEmailId, cancellationToken) switch
        {
            null => OutboxDecisionOutcome.RecordUnknown,
            { Dismissal: not null } => OutboxDecisionOutcome.StageDoesNotAllowIt,
            var record => this.Signalled(
                record,
                await outbox.RequeueAsync(
                    outgoingEmailId,
                    refusalRestated,
                    reachesDismissedSend: false,
                    cancellationToken)),
        };
    }

    /// <summary>Takes one of this user's refused sends, or one whose outcome nobody will learn, out of its account's outbox.</summary>
    /// <param name="outgoingEmailId">The record to dismiss.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the send, which is <see cref="OutboxDecisionOutcome.RecordUnknown" /> for one this user does not hold.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no user.</exception>
    /// <remarks>
    /// It is the person accepting the failure: a refused send stays refused and one whose transmission began keeps an
    /// outcome nobody knows, and the record says who dismissed it and when. It is a decision of its own rather than a
    /// withdrawal, because a refusal can follow a partial acceptance and calling it withdrawn would be untrue.
    /// </remarks>
    public async Task<OutboxDecisionOutcome> DismissAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        var user = authorization.RequireUser();

        return await this.FindAssignedAsync(outgoingEmailId, cancellationToken) is { } record
            ? this.Signalled(record, await outbox.DismissAsync(outgoingEmailId, user, cancellationToken))
            : OutboxDecisionOutcome.RecordUnknown;
    }

    /// <summary>Takes one of this user's refused sends back to the draft they wrote it in, so they can edit it, and out of the outbox.</summary>
    /// <param name="outgoingEmailId">The record to take back.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the send, with the draft it was taken back into where it was.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds neither <see cref="MailFathomPermission.MailSend" /> nor <see cref="MailFathomPermission.MailDraftsWrite" />, or is acting for no user.</exception>
    /// <remarks>
    /// It needs both grants, because it is two acts made one: deciding about a send, and taking a draft back into the
    /// hands that edit it. The draft has to be the caller's own, since a draft is its writer's alone, so a send another
    /// user wrote is refused as one there is no draft to reopen rather than handed to somebody else to edit.
    /// </remarks>
    public async Task<OutboxReopening> ReopenAsDraftAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);
        authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

        var user = authorization.RequireUser();

        if (await this.FindAssignedAsync(outgoingEmailId, cancellationToken) is not { } record)
        {
            return new OutboxReopening(OutboxDecisionOutcome.RecordUnknown, Draft: null);
        }

        var reopening = await outbox.ReopenAsDraftAsync(outgoingEmailId, user, cancellationToken);

        _ = this.Signalled(record, reopening.Outcome);

        return reopening;
    }

    private static EmailContentBody BodyOf(EmailContentRendering rendering) => rendering.BodyIsEncrypted
        ? EmailContentBody.EncryptedNotReadableLocally
        : EmailContentBody.Readable(
            rendering.PlainTextBody,
            rendering.SanitizedHtmlBody,
            rendering.Document,
            rendering.SelfContainedHtmlBody,
            rendering.BodyForms);

    private void RequireOutboxReading()
    {
        authorization.RequirePermission(MailFathomPermission.MailRead);
        authorization.RequirePermission(MailFathomPermission.MailSend);
    }

    private async Task<MailAccountId> ResolveAssignedAsync(
        MailAccountSelector account,
        CancellationToken cancellationToken) =>
        (await accountCatalog.ReadAssignedAccountsAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.IsNamedBy(account))?.Id
        ?? throw new MailAccountNotAccessibleException(account);

    private async Task<OutgoingEmailRecord?> FindAssignedAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var record = await outgoingEmails.FindAsync(outgoingEmailId, cancellationToken);

        return record is not null
            && (await accountCatalog.ReadAssignedAccountsAsync(cancellationToken)).Any(assigned => assigned.Id == record.AccountId)
            ? record
            : null;
    }

    private OutboxDecisionOutcome Signalled(OutgoingEmailRecord record, OutboxDecisionOutcome outcome)
    {
        if (outcome == OutboxDecisionOutcome.Accepted)
        {
            clientSignals.Publish(ClientSignal.OutboxChanged(record.AccountId));
        }

        return outcome;
    }

    private string? StateFailure(MailFathomErrorCode? failure, UserId user) =>
        failure is { } recorded ? languages.GetText(OutboxFailureStatements.TextOf(recorded), user) : null;

    /// <summary>Reads the stored message the way a stored email is read, or nothing where it is missing or unreadable.</summary>
    private async Task<EmailContentRendering?> RenderAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var content = await contentStore.FindOutgoingContentAsync(outgoingEmailId, cancellationToken);

        if (content is null || content.FindIntegrityDefect() is not null)
        {
            return null;
        }

        var rendered = await renderer.RenderAsync(
            content,
            new EmailContentRenderingBounds(
                IncludeSanitizedHtml: false,
                readOptions.MaxBodyCharacters,
                readOptions.MaxCharactersPerRead)
            {
                IncludeMailDocument = true,
                RemainingInlineImageOctetsForRead = MailDocumentBounds.Default.MaximumInlineImageOctetsPerDocument,
            },
            cancellationToken);

        return rendered.Rendering;
    }

    /// <summary>Reads the subject of a send whose record an earlier build wrote without one, from the message it will transmit.</summary>
    /// <remarks>
    /// Bounded to such records, which leave the outbox as they are sent, withdrawn, or dismissed, so the cost of reading
    /// a message to name it is paid only while an older build's sends are still waiting.
    /// </remarks>
    private async Task<string?> ReadSubjectFromMessageAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var content = await contentStore.FindOutgoingContentAsync(outgoingEmailId, cancellationToken);

        return content is null || content.FindIntegrityDefect() is not null || content.RawMime.IsEmpty
            ? null
            : (await textReader.ReadWordsAsync(content.RawMime, cancellationToken)).Subject;
    }

    private async Task<IReadOnlyList<AccountOutboxEntry>> GuardedAsync(
        MailAccountId account,
        List<AccountOutboxEntry> entries,
        CancellationToken cancellationToken)
    {
        using var actingFor = egressGuard.ActingFor(await egressGuard.ReadPostureOfAsync(account, cancellationToken));

        if (entries.Count == 0 || !await egressGuard.IsActiveAsync(cancellationToken))
        {
            return entries;
        }

        // One report for the page rather than one per row, because the page is what a screen waits for.
        using var scan = egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientMailListing,
            cancellationToken);

        var guarded = new List<AccountOutboxEntry>(entries.Count);

        foreach (var entry in entries)
        {
            guarded.Add(entry with
            {
                Subject = await egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientMailListing,
                    entry.Subject,
                    cancellationToken),
            });
        }

        scan.Completed();

        return guarded;
    }

    private async Task<OutboxPreview> GuardedAsync(OutboxPreview preview, CancellationToken cancellationToken)
    {
        using var actingFor = egressGuard.ActingFor(
            await egressGuard.ReadPostureOfAsync(preview.Send.AccountId, cancellationToken));

        if (!await egressGuard.IsActiveAsync(cancellationToken))
        {
            return preview;
        }

        using var scan = egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpEmailContent,
            cancellationToken);

        var guarding = new EmailContentEgressGuarding(egressGuard, SensitiveContentEgressPoint.McpEmailContent);
        var guardedSender = preview.Sender is { } sender
            && (await guarding.GuardAsync(
                new EmailContentHeaders(null, null, null, [sender], EmailThreadReferences.None),
                cancellationToken)).Participants is [var guardedParticipant, ..]
            ? guardedParticipant
            : null;

        var guarded = preview with
        {
            Subject = await egressGuard.GuardOptionalAsync(
                SensitiveContentEgressPoint.McpEmailContent,
                preview.Subject,
                cancellationToken),
            Sender = guardedSender,
            Body = preview.Body is { } body ? await guarding.GuardAsync(body, cancellationToken) : null,
        };

        scan.Completed();

        return guarded;
    }
}
