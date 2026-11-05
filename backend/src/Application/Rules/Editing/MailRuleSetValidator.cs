// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Rules.Editing;

/// <summary>Judges a candidate rule set as a whole, which every write runs before it commits.</summary>
/// <remarks>
/// <para>
/// One validation behind every surface, so <c>mfctl</c> and the client cannot accept different rules. It is the condition
/// compilation and type check under the configured bounds, the rules a declaration already had to keep — each trigger at
/// most once, a schedule exactly when the schedule trigger is declared, a set of actions that can be honored together —
/// and three claims about the account: that it keeps no more than <see cref="MaximumRulesPerAccount" /> rules, that each
/// action is one its <c>RuleActions</c> permits, and that each destination is a folder it maps.
/// </para>
/// <para>
/// A rule the write adds or changes is judged whether it is enabled or not, because a created rule starts disabled and
/// enabling it later must not be the first write to discover its condition never compiled. A refusal names the rule by its identifier and never
/// quotes its name, a folder, or a keyword, because each of those is text a person typed. A condition refusal is the
/// compiler's own reading and may quote the one identifier or function it could not read, and the position it stopped
/// at: that is what tells the writer what to correct, and it goes back only to the caller who wrote the condition. The
/// editor logs no refusal, so none of it reaches a log line, a metric, or a trace.
/// </para>
/// </remarks>
public sealed class MailRuleSetValidator
{
    /// <summary>The most rules one account keeps, enabled or not.</summary>
    /// <remarks>
    /// A fixed bound rather than a setting: nothing about a deployment makes a larger set safer to evaluate on every
    /// arrival, and a bound an operator lowered below a stored set would leave sets nobody may write without first
    /// removing rules.
    /// </remarks>
    public const int MaximumRulesPerAccount = 200;

    /// <summary>The most characters a rule's name may be written in.</summary>
    public const int MaximumNameLength = 128;

    private readonly IMailRuleConditionCompiler compiler;
    private readonly IMailRuleActionPermissionReader permissions;
    private readonly MailFolderReferenceResolver folders;

    /// <summary>Initializes the validator.</summary>
    /// <param name="compiler">Reads each condition against the fact surface.</param>
    /// <param name="permissions">Reads which actions an account permits a rule to take.</param>
    /// <param name="folders">Resolves a destination against the folders an account maps.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailRuleSetValidator(
        IMailRuleConditionCompiler compiler,
        IMailRuleActionPermissionReader permissions,
        MailFolderReferenceResolver folders)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(folders);

        this.compiler = compiler;
        this.permissions = permissions;
        this.folders = folders;
    }

    /// <summary>Reports everything that stops a candidate set from being committed to an account.</summary>
    /// <param name="account">The account the set would belong to, which must be one this deployment serves.</param>
    /// <param name="stored">The set as it stands before the write, empty when the candidate replaces nothing.</param>
    /// <param name="candidate">The whole set as it would stand after the write, in position order.</param>
    /// <param name="bounds">The condition bounds in force.</param>
    /// <returns>One message per defect, empty when the set may be committed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The count and the uniqueness of names and identifiers are claims about the whole set and are judged over all of it.
    /// Every other check is judged only on a rule the write adds, changes, or enables. A rule left as it was stored, or one
    /// the write only disables, is not judged again, because a narrowed <c>RuleActions</c>, an unmapped folder, or a lowered
    /// bound would otherwise refuse every later write to the account — including the one disabling the rule it broke.
    /// </remarks>
    public IReadOnlyList<string> FindErrors(
        MailAccountId account,
        IReadOnlyList<MailRuleDeclaration> stored,
        IReadOnlyList<MailRuleDeclaration> candidate,
        MailRuleConditionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(bounds);

        var permitted = this.permissions.GetRuleActionPermissions(account);
        var storedById = stored.ToDictionary(rule => rule.Id);

        return
        [
            .. FindSetErrors(candidate),
            .. candidate
                .Where(rule => IsJudged(rule, storedById.GetValueOrDefault(rule.Id)))
                .SelectMany(rule => this.FindRuleErrors(account, rule, permitted, bounds)),
        ];
    }

    private static bool IsJudged(MailRuleDeclaration rule, MailRuleDeclaration? stored) =>
        stored is null || (rule.Enabled && !stored.Enabled) || !HasSameContent(rule, stored);

    private static bool HasSameContent(MailRuleDeclaration rule, MailRuleDeclaration stored) =>
        string.Equals(rule.Name, stored.Name, StringComparison.Ordinal)
        && string.Equals(rule.ConditionText, stored.ConditionText, StringComparison.Ordinal)
        && rule.Actions.Select(action => action.CanonicalForm).SequenceEqual(stored.Actions.Select(action => action.CanonicalForm))
        && rule.StopWhenMatched == stored.StopWhenMatched
        && rule.Triggers.SequenceEqual(stored.Triggers)
        && string.Equals(rule.Schedule?.CanonicalForm, stored.Schedule?.CanonicalForm, StringComparison.Ordinal);

    private static IEnumerable<string> FindSetErrors(IReadOnlyList<MailRuleDeclaration> candidate)
    {
        if (candidate.Count > MaximumRulesPerAccount)
        {
            yield return $"An account keeps at most {MaximumRulesPerAccount} rules, and this change would leave it {candidate.Count}.";
        }

        var repeatedName = candidate
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Name))
            .GroupBy(rule => rule.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (repeatedName is not null)
        {
            yield return $"Rules {string.Join(" and ", repeatedName.Select(rule => rule.Id))} carry one name, and a name is unique within an account without regard to case.";
        }

        var repeatedId = candidate.GroupBy(rule => rule.Id).FirstOrDefault(group => group.Count() > 1);

        if (repeatedId is not null)
        {
            yield return $"Rule {repeatedId.Key} appears more than once in the set.";
        }
    }

    private IEnumerable<string> FindRuleErrors(
        MailAccountId account,
        MailRuleDeclaration rule,
        MailRuleActionPermissions permitted,
        MailRuleConditionBounds bounds)
    {
        var ruleName = rule.Id.ToString();

        return
        [
            .. FindNameErrors(rule, ruleName),
            .. FindTriggerErrors(rule, ruleName),
            .. FindActionErrors(rule, ruleName),
            .. this.FindAccountErrors(account, rule, ruleName, permitted),
            .. this.FindConditionErrors(rule, ruleName, bounds),
        ];
    }

    private static IEnumerable<string> FindNameErrors(MailRuleDeclaration rule, string ruleName)
    {
        var name = rule.Name?.Trim() ?? string.Empty;

        if (name.Length is 0 or > MaximumNameLength)
        {
            yield return $"Rule {ruleName} needs a name of 1 to {MaximumNameLength} characters.";
        }
        else if (name.Any(char.IsControl))
        {
            yield return $"Rule {ruleName} has a name carrying a control character.";
        }
    }

    private static IEnumerable<string> FindTriggerErrors(MailRuleDeclaration rule, string ruleName)
    {
        if (rule.Triggers.Any(trigger => !trigger.IsSpecified))
        {
            yield return $"Rule {ruleName} declares a trigger that names nothing.";

            yield break;
        }

        if (rule.Triggers.Distinct().Count() != rule.Triggers.Count)
        {
            yield return $"Rule {ruleName} declares one trigger more than once, and its triggers are a set.";
        }

        var declaresScheduleTrigger = rule.Triggers.Contains(MailRuleTrigger.Schedule);

        if (declaresScheduleTrigger && rule.Schedule is null)
        {
            yield return $"Rule {ruleName} declares the '{MailRuleTrigger.Schedule.Name}' trigger and no schedule, so nothing would ever run it.";
        }
        else if (!declaresScheduleTrigger && rule.Schedule is not null)
        {
            yield return $"Rule {ruleName} declares a schedule and not the '{MailRuleTrigger.Schedule.Name}' trigger, so the occasions it names would run nothing.";
        }
    }

    private static IEnumerable<string> FindActionErrors(MailRuleDeclaration rule, string ruleName)
    {
        if (rule.Actions.Any(action => action is null))
        {
            return [$"Rule {ruleName} declares an action that names nothing."];
        }

        var separated = rule.Actions.Any(action => action.Destination is { } destination
            && MailRuleSetRevision.ContainsSeparator(destination.ToString()));

        return separated
            ? [$"Rule {ruleName} files into a folder named with a separator character a rule set's identity is derived with."]
            : MailRuleActionSet.FindErrors(ruleName, rule.Actions);
    }

    private IEnumerable<string> FindAccountErrors(
        MailAccountId account,
        MailRuleDeclaration rule,
        string ruleName,
        MailRuleActionPermissions permitted)
    {
        var actions = rule.Actions.Where(action => action is not null).ToArray();

        foreach (var action in actions.Where(action => !permitted.Permits(action.Mutation)))
        {
            yield return $"Rule {ruleName} declares '{action.Mutation.Name}', which this account does not permit a rule to do. Its administrator permits it in the account's RuleActions.";
        }

        foreach (var action in actions.Where(action => action.Destination is { } destination && !this.Maps(account, destination)))
        {
            yield return $"Rule {ruleName} declares '{action.Mutation.Name}' into a folder this account maps none for.";
        }
    }

    /// <summary>Reports whether a destination names a folder this account maps, by its alias or the role it plays.</summary>
    /// <remarks>
    /// A destination named by a folder identifier is refused here until a rule's action can file by one.
    /// </remarks>
    private bool Maps(MailAccountId account, MailFolderReference destination) =>
        destination is { Alias: not null } or { Role: not null } && this.folders.TryResolve(account, destination) is not null;

    private IEnumerable<string> FindConditionErrors(
        MailRuleDeclaration rule,
        string ruleName,
        MailRuleConditionBounds bounds)
    {
        if (MailRuleSetRevision.ContainsSeparator(rule.ConditionText))
        {
            return [$"Rule {ruleName} has a condition carrying a separator character a rule set's identity is derived with."];
        }

        return this.compiler.Compile(ruleName, rule.ConditionText, bounds).Errors;
    }
}
