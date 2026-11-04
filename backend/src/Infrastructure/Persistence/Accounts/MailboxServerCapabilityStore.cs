// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Linq.Expressions;
using MailFathom.Application.Accounts;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.Infrastructure.Persistence.Accounts;

/// <summary>Reads and writes the two capability columns of the account row.</summary>
/// <remarks>
/// <para>
/// A recording is one conditional <c>UPDATE</c> rather than a read and a write, because two replicas establishing a
/// session against the same server at once is the ordinary case: PostgreSQL re-evaluates the condition on the row the
/// first writer left, so the later observation stands whichever of the two commits last.
/// </para>
/// <para>
/// Each call opens a scope of its own. A recording is made from inside an IMAP connection's establishment, which a
/// push session repeats on its own schedule after a dropped socket, so borrowing the caller's context could put a
/// second operation on a context that is not safe for one, or a write inside a transaction somebody else opened.
/// </para>
/// </remarks>
internal sealed class MailboxServerCapabilityStore : IMailboxServerCapabilityStore
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the store over the scopes each statement runs in and the clock that stamps an observation.</summary>
    /// <param name="scopeFactory">Opens the scope each statement reaches the database through.</param>
    /// <param name="timeProvider">Stamps when an observation was made.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailboxServerCapabilityStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.scopeFactory = scopeFactory;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [RequiresIntegrationCoverage]
    public async Task<MailboxServerCapabilityObservation> ReadAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var accountIdValue = account.Value;

        await using var scope = this.scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MailFathomDbContext>();

        var recorded = await dbContext.MailboxAccounts
            .AsNoTracking()
            .Where(row => row.Id == accountIdValue)
            .Select(static row => new { row.ServerCapabilities, row.ServerCapabilitiesObservedAt })
            .SingleOrDefaultAsync(cancellationToken);

        return recorded?.ServerCapabilitiesObservedAt is { } observedAt
            ? MailboxServerCapabilityObservation.Observed(recorded.ServerCapabilities, observedAt)
            : MailboxServerCapabilityObservation.NeverObserved;
    }

    /// <inheritdoc />
    [RequiresIntegrationCoverage]
    public async Task RecordAsync(
        MailAccountId account,
        MailboxServerCapabilities advertised,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? observedAt = this.timeProvider.GetUtcNow();

        await using var scope = this.scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MailFathomDbContext>();

        await dbContext.MailboxAccounts
            .Where(ReplaceableBy(account, observedAt.Value))
            .ExecuteUpdateAsync(
                row => row
                    .SetProperty(entity => entity.ServerCapabilities, advertised)
                    .SetProperty(entity => entity.ServerCapabilitiesObservedAt, observedAt),
                cancellationToken);
    }

    /// <summary>Selects the account's row where an observation made at <paramref name="observedAt" /> may replace what it holds.</summary>
    /// <param name="account">The account the observation belongs to.</param>
    /// <param name="observedAt">When the observation was made.</param>
    /// <returns>The condition the recording statement updates under.</returns>
    /// <remarks>
    /// A row no session has recorded on is replaceable by any observation, and a recorded one only by a later one. An
    /// observation at the very instant already stored changes nothing, since it has nothing newer to say.
    /// </remarks>
    internal static Expression<Func<MailboxAccountEntity, bool>> ReplaceableBy(
        MailAccountId account,
        DateTimeOffset observedAt)
    {
        var accountIdValue = account.Value;

        return row => row.Id == accountIdValue
            && (row.ServerCapabilitiesObservedAt == null || row.ServerCapabilitiesObservedAt < observedAt);
    }
}
