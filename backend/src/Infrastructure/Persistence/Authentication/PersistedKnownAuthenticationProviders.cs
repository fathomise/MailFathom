// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.KnownAuthenticationProviders;
using MailFathom.Application.Paging;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Authentication;

/// <summary>The known authentication providers and their change record, kept in PostgreSQL.</summary>
/// <remarks>
/// Every write is one transaction holding the entry and its change record together, so the record never names a change
/// that did not happen and never misses one that did. Two replicas adding the same host meet the unique host index:
/// the insert of the one that lost does nothing, and it reports the host as already listed.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedKnownAuthenticationProviders(MailFathomDbContext dbContext)
    : IKnownAuthenticationProviderStore, IKnownAuthenticationProviderDirectory
{
    /// <inheritdoc />
    public async Task<KnownAuthenticationProvider?> FindAsync(string imapHost, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imapHost);

        var entry = await dbContext.KnownAuthenticationProviders
            .AsNoTracking()
            .Where(provider => provider.ImapHost == imapHost)
            .Select(provider => new { provider.ImapHost, provider.AuthorityIdentifier })
            .SingleOrDefaultAsync(cancellationToken);

        return entry is null ? null : new KnownAuthenticationProvider(entry.ImapHost, entry.AuthorityIdentifier);
    }

    /// <inheritdoc />
    public async Task<KnownAuthenticationProviderListing> ReadAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var providers = dbContext.KnownAuthenticationProviders.AsNoTracking();

        if (query.After is { } after)
        {
            providers = providers.Where(provider => provider.Id > after);
        }

        var entries = await providers
            .OrderBy(provider => provider.Id)
            .Take(query.PageSize + 1)
            .Select(provider => new ListedKnownAuthenticationProvider(
                provider.Id,
                provider.ImapHost,
                provider.AuthorityIdentifier,
                provider.AddedBy,
                provider.AddedAt))
            .ToArrayAsync(cancellationToken);

        var page = query.PageOf(entries, entry => entry.Id);

        return new KnownAuthenticationProviderListing(page.Entries, page.ContinuesAfter);
    }

    /// <inheritdoc />
    public async Task<bool> AddAsync(
        KnownAuthenticationProvider provider,
        string changedBy,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(changedBy);

        var id = Guid.CreateVersion7(at);

        await using var addition = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The identifier is freshly minted, so the host's index is the one constraint a loser can meet.
        var written = await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO known_authentication_providers ("Id", "ImapHost", "AuthorityIdentifier", "AddedBy", "AddedAt")
             VALUES ({id}, {provider.ImapHost}, {provider.AuthorityIdentifier}, {changedBy}, {at})
             ON CONFLICT DO NOTHING
             """,
            cancellationToken);

        if (written != 1)
        {
            return false;
        }

        this.RecordChange(provider.ImapHost, provider.AuthorityIdentifier, KnownAuthenticationProviderChangeEntity.Added, changedBy, at);
        await dbContext.SaveChangesAsync(cancellationToken);
        await addition.CommitAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        string imapHost,
        string changedBy,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imapHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(changedBy);

        await using var removal = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The delete returns the row it removed, so the change record names exactly what this transaction took away and
        // a concurrent removal of the same host finds nothing and records nothing.
        var removed = await dbContext.Database
            .SqlQuery<string>(
                $"""
                 DELETE FROM known_authentication_providers
                 WHERE "ImapHost" = {imapHost}
                 RETURNING "AuthorityIdentifier" AS "Value"
                 """)
            .ToArrayAsync(cancellationToken);

        if (removed is not [var authorityIdentifier])
        {
            return false;
        }

        this.RecordChange(imapHost, authorityIdentifier, KnownAuthenticationProviderChangeEntity.Removed, changedBy, at);
        await dbContext.SaveChangesAsync(cancellationToken);
        await removal.CommitAsync(cancellationToken);

        return true;
    }

    private void RecordChange(
        string imapHost,
        string authorityIdentifier,
        string act,
        string changedBy,
        DateTimeOffset at) =>
        dbContext.KnownAuthenticationProviderChanges.Add(new KnownAuthenticationProviderChangeEntity
        {
            Id = Guid.CreateVersion7(at),
            ImapHost = imapHost,
            AuthorityIdentifier = authorityIdentifier,
            Act = act,
            ChangedBy = changedBy,
            ChangedAt = at,
        });
}
