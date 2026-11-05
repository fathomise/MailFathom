// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Opens, reads, and sets the stored half of the records a mirrored account's act commits locally first.</summary>
/// <remarks>
/// The record store opens the record and writes the observations onto it, and the stored value the act wrote is the
/// local state store's. A test about the record store alone writes that value itself, so what it proves is the store's
/// treatment of the field rather than the act's whole path, which the unit suite covers.
/// </remarks>
internal static class OverlayingMutationRecords
{
    /// <summary>An overlay that observed nothing, for an act that overlays no field the test reads.</summary>
    internal static MailboxMutationOverlay NothingObserved { get; } = new(null, null, null, null, null);

    /// <summary>Opens one overlaying record in a session of its own, committed.</summary>
    internal static Task<OpenedMailboxMutationRecord> OpenAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        MailboxMutationOverlay replaced,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .OpenOverlayAsync(session, request, heldUntil: null, replaced, token),
            cancellationToken);

    /// <summary>Reads one record as a caller assigned the synthetic account sees it, whatever stage it is at.</summary>
    internal static async Task<MailboxMutationRecord> ReadAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken) => Assert.Single(await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .ReadAsync([SyntheticMailAccount.AccountId], [recordId], token),
            cancellationToken));

    /// <summary>Reads what a convergence pass is handed, narrowed to the records a test opened.</summary>
    internal static async Task<IReadOnlyList<MailboxMutationRecordId>> ReadEligibleAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyCollection<MailboxMutationRecordId> opened,
        CancellationToken cancellationToken)
    {
        var outstanding = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .ReadOutstandingAsync(SyntheticMailAccount.Account, limit: 1000, token),
            cancellationToken);

        return [.. outstanding.Select(candidate => candidate.Record.Id).Where(recordId => opened.Contains(recordId))];
    }

    /// <summary>Writes the stored value an act committed, which the record store leaves to the local state store.</summary>
    internal static async Task SetStoredStateAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        Action<StoredEmailEntity> write,
        CancellationToken cancellationToken)
    {
        var identity = storedEmailId.Value;

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();
                var row = await dbContext.StoredEmails.SingleAsync(email => email.Id == identity, token);

                write(row);

                return await dbContext.SaveChangesAsync(token);
            },
            cancellationToken);
    }
}
