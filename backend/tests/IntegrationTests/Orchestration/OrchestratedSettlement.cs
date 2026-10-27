// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Leaves on stored messages what an account run's settlement leaves on the ones it has settled.</summary>
/// <remarks>
/// Every read a person or an agent makes admits settled mail alone, so a message a fixture stores and never settles is
/// one no listing, search, or read by identifier returns — and a test asserting that one did would be asserting the
/// settlement is broken. Written as a set-based update rather than through the pass itself, because what a fixture
/// needs is the state the pass leaves rather than its spam and rule stages, which a fixture seeding mail has not run.
/// </remarks>
internal static class OrchestratedSettlement
{
    /// <summary>Marks stored messages as settled, at the instant the caller names.</summary>
    /// <param name="services">The orchestrated services the update runs through.</param>
    /// <param name="storedEmailIds">The messages to settle.</param>
    /// <param name="settledAt">When they are recorded as having settled.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when every row carries the settlement.</returns>
    internal static Task ApplyAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyCollection<StoredEmailId> storedEmailIds,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        Guid[] identities = [.. storedEmailIds.Select(static email => email.Value)];

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .Where(email => identities.Contains(email.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(email => email.SettledAt, settledAt), token),
            cancellationToken);
    }
}
