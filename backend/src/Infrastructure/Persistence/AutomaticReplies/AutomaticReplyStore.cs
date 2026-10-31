// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.AutomaticReplies;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.AutomaticReplies;

[RequiresIntegrationCoverage]
internal sealed class AutomaticReplyStore(MailFathomDbContext context, TimeProvider clock) : IAutomaticReplyStore
{
    /// <inheritdoc />
    public async Task<StoredAutomaticReply?> ReadAsync(UserId user, CancellationToken cancellationToken)
    {
        var userValue = user.Value;

        var row = await context.AutomaticReplies
            .AsNoTracking()
            .FirstOrDefaultAsync(reply => reply.UserId == userValue, cancellationToken);

        return row is null ? null : Restore(row);
    }

    /// <inheritdoc />
    public async Task<StoredAutomaticReply?> SaveAsync(
        UserId user,
        AutomaticReply reply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var revisions = await context.Database
            .SqlQuery<Guid>(AutomaticReplyStatements.ComposeSave(user.Value, Guid.CreateVersion7(), reply, clock.GetUtcNow()))
            .ToArrayAsync(cancellationToken);

        return revisions is [var revision] ? new StoredAutomaticReply(reply, revision) : null;
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(
        UserId user,
        Guid revision,
        EmailAddress sender,
        CancellationToken cancellationToken)
    {
        var inserted = await context.Database.ExecuteSqlAsync(
            AutomaticReplyStatements.ComposeClaim(user.Value, revision, sender.NormalizedAddress, clock.GetUtcNow()),
            cancellationToken);

        return inserted > 0;
    }

    /// <summary>Reads a stored row back into the reply it was saved from.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the row is not a reply, which is a row something other than this store wrote.</exception>
    private static StoredAutomaticReply Restore(AutomaticReplyEntity row)
    {
        if (!AutomaticReply.TryCreate(
                row.InsideText,
                row.OutsideText,
                [.. row.AccountIds.Select(MailAccountId.Create)],
                row.StartsAt,
                row.EndsAt,
                row.IsOn,
                out var reply,
                out _))
        {
            throw new InvalidOperationException($"The automatic reply stored for user {row.UserId} is not one this build reads.");
        }

        return new StoredAutomaticReply(reply!, row.Revision);
    }
}
