// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Frozen;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Application.Rules;

/// <summary>One enabled rule of a mail account's set: its identity, its name, a compiled condition, and what a match does.</summary>
/// <remarks>
/// Carries a compiled condition rather than the text it was written from, so nothing downstream of reading the set can
/// read the condition a person typed. Its name is authored text too, and may carry an address: it travels into the run
/// record a match writes, and never into a log, a metric, or a trace. It names no account, because the set it was read
/// from belongs to one.
/// </remarks>
public sealed class MailRule
{
    private MailRule(
        MailRuleId id,
        string name,
        IMailRuleCondition condition,
        MailRuleActionSet actions,
        bool stopWhenMatched,
        FrozenSet<MailRuleTrigger> triggers,
        JobRecurrence? schedule)
    {
        this.Id = id;
        this.Name = name;
        this.Condition = condition;
        this.Actions = actions;
        this.StopWhenMatched = stopWhenMatched;
        this.Triggers = triggers;
        this.Schedule = schedule;
    }

    /// <summary>Gets the identifier the rule was created with, which an execution record names it by beside its name.</summary>
    public MailRuleId Id { get; }

    /// <summary>Gets the name the rule carried when the set was read.</summary>
    public string Name { get; }

    /// <summary>Gets the condition an email is matched against.</summary>
    public IMailRuleCondition Condition { get; }

    /// <summary>Gets what a match does to the matching email, in the order the changes are applied.</summary>
    /// <remarks>
    /// Empty for a rule that selects mail and changes nothing, which is what a rule ending the pass declares to keep the
    /// mail it names away from the rules below it.
    /// </remarks>
    public MailRuleActionSet Actions { get; }

    /// <summary>Gets whether a match ends the pass rather than continuing to the rules below this one.</summary>
    public bool StopWhenMatched { get; }

    /// <summary>Gets the automatic triggers this rule takes part in, empty for a rule only a requested walk runs.</summary>
    /// <remarks>
    /// Empty means no automatic occasion rather than every one: a rule nothing fires by itself is exactly what periodic
    /// housekeeping wants. The rule stays in the set, is validated like every other, and runs when somebody asks for a
    /// walk of the whole mailbox.
    /// </remarks>
    public IReadOnlySet<MailRuleTrigger> Triggers { get; }

    /// <summary>Gets the occasions a scheduled walk of this rule happens on, and <see langword="null" /> when it declares no schedule.</summary>
    /// <remarks>
    /// Present exactly when <see cref="MailRuleTrigger.Schedule" /> is declared, which a write validating the set makes
    /// true rather than one read here: a schedule without the trigger names occasions nothing acts on, and the trigger
    /// without a schedule is a rule that would never fire.
    /// </remarks>
    public JobRecurrence? Schedule { get; }

    /// <summary>Creates a rule from a condition that has already been proven usable.</summary>
    /// <param name="id">The identifier the rule was created with.</param>
    /// <param name="name">The name the rule carries.</param>
    /// <param name="condition">The compiled condition.</param>
    /// <param name="actions">What a match does to the matching email, or nothing for a rule that changes nothing.</param>
    /// <param name="stopWhenMatched">Whether a match ends the pass.</param>
    /// <param name="triggers">
    /// The automatic triggers the rule takes part in, or nothing for a rule only a requested walk runs. An empty list
    /// and <see langword="null" /> say the same thing, because a rule takes part in the occasions it names and in no
    /// others.
    /// </param>
    /// <param name="schedule">The occasions a scheduled walk happens on, required by and only by the schedule trigger.</param>
    /// <returns>The rule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="condition" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id" /> is unspecified, <paramref name="name" /> is empty or whitespace, a trigger is
    /// unspecified, or the schedule and the trigger that uses it do not agree.
    /// </exception>
    public static MailRule Create(
        MailRuleId id,
        string name,
        IMailRuleCondition condition,
        MailRuleActionSet? actions = null,
        bool stopWhenMatched = false,
        IReadOnlyList<MailRuleTrigger>? triggers = null,
        JobRecurrence? schedule = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(condition);

        if (!id.IsSpecified)
        {
            throw new ArgumentException("A rule must carry the identifier it was created with.", nameof(id));
        }

        if (triggers?.Any(trigger => !trigger.IsSpecified) == true)
        {
            throw new ArgumentException("A rule cannot declare a trigger that is unspecified.", nameof(triggers));
        }

        var declaredTriggers = triggers is null ? FrozenSet<MailRuleTrigger>.Empty : triggers.ToFrozenSet();

        if (declaredTriggers.Contains(MailRuleTrigger.Schedule) != (schedule is not null))
        {
            throw new ArgumentException(
                $"A rule declares a schedule if and only if it declares the '{MailRuleTrigger.Schedule.Name}' trigger.",
                nameof(schedule));
        }

        return new MailRule(
            id,
            name,
            condition,
            actions ?? MailRuleActionSet.Empty,
            stopWhenMatched,
            declaredTriggers,
            schedule);
    }

    /// <summary>Reports whether this rule is one an automatic trigger runs.</summary>
    /// <param name="trigger">The trigger the walk in progress was started by.</param>
    /// <returns><see langword="true" /> when the rule declares this trigger.</returns>
    /// <remarks>
    /// A rule the trigger does not reach is passed over rather than evaluated and leaves no evaluation behind: it did not
    /// decline to match, it was not one of this walk's rules. It follows that such a rule cannot end the pass either,
    /// whatever it declares.
    /// </remarks>
    public bool RunsOn(MailRuleTrigger trigger) => this.Triggers.Contains(trigger);
}
