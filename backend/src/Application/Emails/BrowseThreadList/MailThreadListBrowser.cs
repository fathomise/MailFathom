// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>Serves one page of a person's conversations, which is what a mail screen reading by thread draws.</summary>
/// <remarks>
/// <para>
/// A conversation is read across every folder the person may read, junk included, and narrowed by account alone: it
/// is listed under the account that holds it, or under all of the person's accounts. What it is quoted by — its
/// newest message, the sender mark of its newest received one, who wrote into it, and what a derivation called it — is
/// read for the page rather than per row, so a page of fifty costs a handful of reads rather than fifty.
/// </para>
/// <para>
/// Who the reader is comes from the addresses their assigned accounts send as, which is the same answer outgoing mail
/// vouches for its own addresses by. An account with no sending identity contributes none, so on a read-only account
/// every message counts as received.
/// </para>
/// </remarks>
public sealed class MailThreadListBrowser
{
    private readonly IEmailThreadListReader listReader;
    private readonly IStoredEmailSummaryReader summaryReader;
    private readonly IStoredEmailPreviewReader previewReader;
    private readonly ICallerMailAccountCatalog accountCatalog;
    private readonly IOutgoingSenderIdentityReader senderIdentities;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IMailboxReadTelemetry readTelemetry;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes a new instance of the <see cref="MailThreadListBrowser" /> class.</summary>
    /// <param name="listReader">Reads the conversations of one page.</param>
    /// <param name="summaryReader">Describes the messages a page quotes.</param>
    /// <param name="previewReader">Reads the opening of each quoted message's own text.</param>
    /// <param name="accountCatalog">Names the accounts the caller is assigned, whose sending identities say who the reader is.</param>
    /// <param name="senderIdentities">Reads the address each of those accounts sends as.</param>
    /// <param name="scopeResolver">Resolves what the caller may read.</param>
    /// <param name="egressGuard">Guards what leaves the deployment for the client.</param>
    /// <param name="readTelemetry">Reports the read.</param>
    /// <param name="authorization">Requires the grant a mail read needs.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailThreadListBrowser(
        IEmailThreadListReader listReader,
        IStoredEmailSummaryReader summaryReader,
        IStoredEmailPreviewReader previewReader,
        ICallerMailAccountCatalog accountCatalog,
        IOutgoingSenderIdentityReader senderIdentities,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        IMailboxReadTelemetry readTelemetry,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(listReader);
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(previewReader);
        ArgumentNullException.ThrowIfNull(accountCatalog);
        ArgumentNullException.ThrowIfNull(senderIdentities);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(readTelemetry);
        ArgumentNullException.ThrowIfNull(authorization);

        this.listReader = listReader;
        this.summaryReader = summaryReader;
        this.previewReader = previewReader;
        this.accountCatalog = accountCatalog;
        this.senderIdentities = senderIdentities;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.readTelemetry = readTelemetry;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of the caller's conversations.</summary>
    /// <param name="request">The account, the page size, and the cursor.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The page, empty where the caller is assigned no account.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant does not carry <c>mailfathom.mail.read</c>.</exception>
    /// <exception cref="MailboxQueryPageSizeOutOfRangeException">Thrown when the page size is outside the bound.</exception>
    /// <exception cref="MailboxQueryCursorMalformedException">Thrown when the cursor is not one this deployment issues.</exception>
    /// <exception cref="MailboxQueryCursorFilterMismatchException">Thrown when the cursor was issued for another account's list.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the account is not one the caller is assigned.</exception>
    public async Task<BrowsedThreadList> BrowsePageAsync(
        BrowseThreadListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        using var read = this.readTelemetry.BeginRead(MailboxReadOperation.ListEmailThreads, cancellationToken);

        using var actingFor = this.egressGuard.ActingFor(
            await this.egressGuard.ReadPostureAcrossAccountsOfAsync(this.scopeResolver.User, cancellationToken));

        var pageSize = MailboxQueryPageSize.FromRequested(request.PageSize);
        var fingerprint = EmailThreadListCursor.FingerprintOf(request.Account);
        var boundary = ContinuationBoundary(request.Cursor, fingerprint);

        var scope = await this.scopeResolver.ReadableScopeAsync(
            MailboxReaderKind.Person,
            request.Account is { } account ? [account] : [],
            [],
            JunkMailInclusion.Included,
            cancellationToken);

        if (scope.AccountIds.Count is 0)
        {
            read.Completed(0);

            return new BrowsedThreadList([], NextCursor: null, pageSize.Value);
        }

        var walked = await this.listReader.ReadPageAsync(
            scope,
            await this.ReaderAddressesAsync(cancellationToken),
            boundary,
            pageSize.Value + 1,
            cancellationToken);

        var page = walked.Take(pageSize.Value).ToArray();
        var rows = await this.GuardedAsync(await this.DescribedAsync(page, cancellationToken), cancellationToken);

        read.Completed(rows.Count);

        var nextCursor = walked.Count > pageSize.Value
            ? EmailThreadListCursor.After(page[^1].Position, fingerprint).Encode()
            : null;

        return new BrowsedThreadList(rows, nextCursor, pageSize.Value);
    }

    private static EmailThreadListPosition? ContinuationBoundary(string? cursor, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        if (!EmailThreadListCursor.TryDecode(cursor, out var decodedCursor))
        {
            throw new MailboxQueryCursorMalformedException();
        }

        if (!string.Equals(decodedCursor.FilterFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new MailboxQueryCursorFilterMismatchException();
        }

        return decodedCursor.Position;
    }

    private async Task<IReadOnlySet<EmailAddress>> ReaderAddressesAsync(CancellationToken cancellationToken) =>
        (await this.accountCatalog.ReadAssignedAccountsAsync(cancellationToken))
        .Select(account => this.senderIdentities.FindSenderIdentity(account.Id))
        .OfType<OutgoingSenderIdentity>()
        .Select(static identity => identity.Address)
        .ToHashSet();

    /// <summary>Describes the messages each conversation is quoted by, dropping one whose newest message has gone since it was listed.</summary>
    private async Task<IReadOnlyList<BrowsedListedThread>> DescribedAsync(
        IReadOnlyList<ListedEmailThread> page,
        CancellationToken cancellationToken)
    {
        StoredEmailId[] quoted =
        [
            .. page
                .SelectMany(static thread => new[] { thread.NewestEmailId, thread.NewestReceivedEmailId })
                .OfType<StoredEmailId>()
                .Distinct(),
        ];
        StoredEmailId[] newest = [.. page.Select(static thread => thread.NewestEmailId)];

        var summaries = await this.summaryReader.ReadSummariesAsync(quoted, cancellationToken);
        var previews = await this.previewReader.ReadPreviewsAsync(newest, cancellationToken);

        return
        [
            .. page
                .Where(thread => summaries.ContainsKey(thread.NewestEmailId))
                .Select(thread => new BrowsedListedThread(
                    thread with { Participants = [.. thread.Participants.Take(IEmailThreadListReader.MaximumNamedParticipants)] },
                    thread.Participants.Count > IEmailThreadListReader.MaximumNamedParticipants,
                    summaries[thread.NewestEmailId],
                    previews.TryGetValue(thread.NewestEmailId, out var preview) ? EmailPreview.Bounded(preview) : null,
                    thread.NewestReceivedEmailId is { } received && summaries.TryGetValue(received, out var receivedEmail)
                        ? receivedEmail.SenderVerification
                        : null)),
        ];
    }

    private async Task<IReadOnlyList<BrowsedListedThread>> GuardedAsync(
        IReadOnlyList<BrowsedListedThread> rows,
        CancellationToken cancellationToken)
    {
        if (!await this.egressGuard.IsActiveAsync(cancellationToken))
        {
            return rows;
        }

        // One report for the page rather than one per row, because the page is what a screen waits for.
        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientMailListing,
            cancellationToken);

        var guarded = new List<BrowsedListedThread>(rows.Count);

        foreach (var row in rows)
        {
            guarded.Add(row with
            {
                Thread = row.Thread with
                {
                    Participants = await this.GuardedParticipantsAsync(row.Thread.Participants, cancellationToken),
                    Title = await this.GuardedTitleAsync(row.Thread.Title, cancellationToken),
                },
                Newest = row.Newest with
                {
                    Subject = await this.GuardedTextAsync(row.Newest.Subject, cancellationToken),
                    SenderDisplayName = await this.GuardedTextAsync(row.Newest.SenderDisplayName, cancellationToken),
                },
                NewestPreview = await this.GuardedTextAsync(row.NewestPreview, cancellationToken),
            });
        }

        scan.Completed();

        return guarded;
    }

    private async Task<IReadOnlyList<ListedThreadParticipant>> GuardedParticipantsAsync(
        IReadOnlyList<ListedThreadParticipant> participants,
        CancellationToken cancellationToken)
    {
        var guarded = new List<ListedThreadParticipant>(participants.Count);

        foreach (var participant in participants)
        {
            guarded.Add(participant with
            {
                DisplayName = await this.GuardedTextAsync(participant.DisplayName, cancellationToken),
            });
        }

        return guarded;
    }

    private async Task<ListedThreadTitle?> GuardedTitleAsync(ListedThreadTitle? title, CancellationToken cancellationToken) =>
        title is null
            ? null
            : title with
            {
                Title = title.Title with
                {
                    Topic = await this.GuardedTextAsync(title.Title.Topic, cancellationToken) ?? string.Empty,
                    Standing = await this.GuardedTextAsync(title.Title.Standing, cancellationToken) ?? string.Empty,
                },
            };

    private Task<string?> GuardedTextAsync(string? text, CancellationToken cancellationToken) =>
        this.egressGuard.GuardOptionalAsync(
            SensitiveContentEgressPoint.ClientMailListing,
            text,
            cancellationToken);
}
