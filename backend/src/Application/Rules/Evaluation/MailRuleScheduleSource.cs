// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules.Sets;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>Reads the recurring dispatches the stored rules ask for, one per enabled rule that declares a schedule.</summary>
/// <remarks>
/// <para>
/// A rule belongs to one account, so a scheduled rule is one walk of one mailbox, under an identity composed of the
/// account's generated identifier and the rule's: <c>mail-rules:{account}:{rule}</c>. Both are generated, so renaming a
/// rule neither orphans its schedule nor starts a second one.
/// </para>
/// <para>
/// What a scheduled walk then evaluates is every rule of that account declaring the schedule trigger, not only the rule
/// whose occasion started it. That is one walk of a mailbox per occasion instead of one per rule, which is the whole of
/// the saving: a mailbox is read once and every rule that opted into schedules is applied to what was read. A rule
/// declaring the shorter interval therefore brings the others round with it, which is a deliberate trade and is what
/// <c>Triggers</c> already means everywhere else — the trigger decides which rules a walk reaches, and the schedule
/// decides when a walk starts.
/// </para>
/// <para>
/// Read from the stored rules rather than held, so a write that adds, changes, disables, or removes a schedule reaches
/// the next reconciliation on every replica. A schedule a write withdrew simply stops being declared; the row recording
/// what it last did stays behind, which is what makes putting the rule back a resumption rather than a fresh start.
/// </para>
/// </remarks>
public sealed class MailRuleScheduleSource : IScheduledJobSource
{
    /// <summary>The word every schedule this source declares is prefixed with, so a key says what declared it.</summary>
    private const string IdentityPrefix = "mail-rules";

    private readonly IMailRuleSetStore store;
    private readonly IDeploymentMailAccountCatalog accounts;

    /// <summary>Initializes the source over the stored rules and the accounts this deployment serves.</summary>
    /// <param name="store">Keeps each account's rules.</param>
    /// <param name="accounts">Names the accounts this deployment serves, which a schedule must belong to.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailRuleScheduleSource(IMailRuleSetStore store, IDeploymentMailAccountCatalog accounts)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(accounts);

        this.store = store;
        this.accounts = accounts;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The served accounts are read only where some rule declares a schedule, which is not the ordinary deployment. A
    /// rule of an account the deployment no longer serves declares nothing.
    /// </remarks>
    public async Task<IReadOnlyList<ScheduledJob>> ReadSchedulesAsync(CancellationToken cancellationToken)
    {
        var scheduled = await this.store.ReadScheduledRulesAsync(cancellationToken);

        if (scheduled.Count == 0)
        {
            return [];
        }

        var servedAccounts = (await this.accounts.ReadServedAccountsAsync(cancellationToken))
            .Select(account => account.Id)
            .ToHashSet();

        return [.. scheduled.Where(rule => servedAccounts.Contains(rule.Account)).Select(Declare)];
    }

    /// <summary>Declares one rule's schedule, as the repeated work a dispatch reads.</summary>
    /// <remarks>
    /// No user stands in the identity: a mailbox two people are assigned declares one schedule rather than one each, so
    /// the rule runs over that mail once however many people read it.
    /// </remarks>
    private static ScheduledJob Declare(ScheduledMailRule rule) => new(
        JobScheduleId.Create($"{IdentityPrefix}:{rule.Account.Value}:{rule.Rule}"),
        RunScheduledMailRulesJobPayload.For(rule.Account),
        rule.Schedule,
        rule.Account);
}
