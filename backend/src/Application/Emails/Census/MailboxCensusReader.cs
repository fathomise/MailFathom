// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Categories;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.Census;

/// <summary>Counts the local mailbox copy, in total or grouped, as one bounded reading.</summary>
/// <param name="censusReader">Counts the stored mail.</param>
/// <param name="scopeResolver">Decides which accounts and folders the count runs against.</param>
/// <param name="readTelemetry">Publishes the count as the read it is.</param>
/// <param name="authorization">Answers which principal reached this use case.</param>
/// <param name="offeredCategories">Reads a category filter against what the accounts offer, and says which categories each one shows.</param>
/// <param name="time">The clock the count's time bound runs on.</param>
/// <remarks>
/// <para>
/// A question that wants a figure — how much mail of a kind, who writes most, which months were busiest — is answered by
/// counting rather than by reading a sample, because no page of mail is the mailbox. The database counts it, in one
/// statement over the columns a group is keyed by, and nothing here reads a body.
/// </para>
/// <para>
/// One reading is bounded twice. It carries at most <see cref="MaximumLeadingGroups" /> groups, with how many groups
/// and messages lie beyond them, so what it costs a caller does not follow the size of the mailbox. And it may take at
/// most <see cref="MaximumDuration" />, so a count over a very large scope ends rather than holding a connection: a count
/// that runs out of time answers <see langword="null" /> and the caller narrows the filters and asks again.
/// </para>
/// <para>
/// Nothing here guards a key for egress, because a census leaves this use case for more than one destination and each
/// guards at its own egress point; a sender's address and its domain are what somebody else wrote, which
/// <see cref="MailCensusGroup" /> states for every caller.
/// </para>
/// </remarks>
public sealed class MailboxCensusReader(
    IStoredEmailCensusReader censusReader,
    MailboxScopeResolver scopeResolver,
    IMailboxReadTelemetry readTelemetry,
    AccessAuthorization authorization,
    OfferedMailCategories offeredCategories,
    TimeProvider time)
{
    /// <summary>The number of groups a reading carries when the caller names none.</summary>
    public const int DefaultLeadingGroups = 20;

    /// <summary>The most groups one reading carries.</summary>
    public const int MaximumLeadingGroups = 50;

    /// <summary>The longest a count may take before it is abandoned.</summary>
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(10);

    /// <summary>Counts the mail a request selects.</summary>
    /// <param name="request">What the caller asked for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The census, or <see langword="null" /> where the count did not finish within <see cref="MaximumDuration" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="MailboxQueryPageSizeOutOfRangeException">Thrown when the request names a group ceiling outside the accepted range.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when a filter carries a value, a count, or a length the query does not accept.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <remarks>The grant is asked for before anything is validated, for the reason a listing asks first.</remarks>
    public async Task<MailCensus?> CountAsync(CountEmailsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        authorization.RequirePermission(MailFathomPermission.MailRead);

        using var read = readTelemetry.BeginRead(MailboxReadOperation.CountMailbox, cancellationToken);

        var leadingGroups = request.MaximumGroups ?? DefaultLeadingGroups;

        if (leadingGroups is < 1 or > MaximumLeadingGroups)
        {
            throw new MailboxQueryPageSizeOutOfRangeException(leadingGroups, MaximumLeadingGroups);
        }

        var selection = await this.ReadableSelectionAsync(request, cancellationToken);

        if (selection.Scope.AccountIds.Count is 0)
        {
            read.Completed(0);

            return MailCensus.Empty;
        }

        var grouping = new MailCensusGrouping(
            request.Dimension,
            request.Zone,
            request.Dimension is MailCensusDimension.Category ? offeredCategories.ShownWithin(selection.Scope) : new Dictionary<MailAccountId, IReadOnlyList<string>>());

        using var deadline = new CancellationTokenSource(MaximumDuration, time);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            var census = await censusReader.CountAsync(selection, grouping, leadingGroups, bounded.Token);

            read.Completed(census.Groups.Count);

            return census;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Validates the request's filters against the accounts and folders the caller may read.</summary>
    private async Task<MailboxEmailSelection> ReadableSelectionAsync(CountEmailsRequest request, CancellationToken cancellationToken)
    {
        var scope = await scopeResolver.ReadableScopeAsync(
            request.Reader,
            request.Accounts,
            request.Folders,
            JunkMailInclusion.Excluded,
            cancellationToken);

        return MailboxEmailSelection.Create(
            scope,
            request.SenderAddress,
            request.RecipientAddress,
            request.SubjectFragment,
            request.ReceivedOnOrAfter,
            request.ReceivedBefore,
            request.IsMarkedSeen,
            request.IsFlagged,
            request.Keyword,
            request.HasAttachments,
            mark: null,
            category: offeredCategories.SelectionWithin(scope, request.Category));
    }
}
