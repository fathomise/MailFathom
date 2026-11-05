// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Cli.Commands;

namespace MailFathom.Cli.Administration.Rules;

/// <summary>What a deployment reports about one mail account's stored rules.</summary>
/// <param name="Account">The account the rules belong to.</param>
/// <param name="Version">The set's version, which a write states it read.</param>
/// <param name="Revision">The derived identity of the enabled rules, which every recorded execution names.</param>
/// <param name="Held">Whether no pass runs the set, because an enabled rule no longer compiles.</param>
/// <param name="UncompiledRules">The enabled rules that hold the set, by identifier.</param>
/// <param name="Rules">Every stored rule, disabled ones included, in the order they are evaluated in.</param>
internal sealed record LoadedRuleSet(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("revision")] string? Revision,
    [property: JsonPropertyName("held")] bool Held,
    [property: JsonPropertyName("uncompiledRules")] IReadOnlyList<Guid>? UncompiledRules,
    [property: JsonPropertyName("rules")] IReadOnlyList<LoadedRule>? Rules);

/// <summary>One stored rule of an account.</summary>
/// <param name="Id">The identifier the rule keeps through every rename and edit.</param>
/// <param name="Position">Where the rule sits in its account's order, counted from zero.</param>
/// <param name="Name">The name a person reads it under.</param>
/// <param name="Enabled">Whether a pass applies it.</param>
/// <param name="Condition">The condition as it was written.</param>
/// <param name="Actions">What a match asks the mailbox for, in the order the rule declares the changes.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass rather than continuing to the rules below.</param>
/// <param name="Triggers">The automatic triggers it takes part in, empty for a rule only a requested run applies.</param>
/// <param name="Schedule">The occasions it declares, absent for a rule declaring none.</param>
internal sealed record LoadedRule(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("condition")] string? Condition,
    [property: JsonPropertyName("actions")] IReadOnlyList<LoadedRuleAction>? Actions,
    [property: JsonPropertyName("stopWhenMatched")] bool StopWhenMatched,
    [property: JsonPropertyName("triggers")] IReadOnlyList<string>? Triggers,
    [property: JsonPropertyName("schedule")] string? Schedule)
{
    /// <summary>Describes whether a pass applies the rule, in one word an operator reads.</summary>
    /// <returns><c>enabled</c> or <c>disabled</c>.</returns>
    internal string DescribeState() => this.Enabled ? "enabled" : "disabled";

    /// <summary>Describes what runs the rule in one line an operator reads.</summary>
    /// <returns>The triggers it takes part in, or that a requested run is the only thing that applies it.</returns>
    /// <remarks>
    /// A rule nothing fires by itself is the one an operator is most likely to be asking about — it looks identical to
    /// a rule that never matched — so the answer says what does run it rather than reporting an empty list.
    /// </remarks>
    internal string DescribeTriggers() => (this.Triggers, this.Schedule) switch
    {
        ({ Count: > 0 } triggers, { Length: > 0 } schedule) => $"{string.Join(", ", triggers)} ({schedule})",
        ({ Count: > 0 } triggers, _) => string.Join(", ", triggers),
        _ => $"nothing automatically; '{CliRootCommand.CommandName} rules run' applies it",
    };

    /// <summary>Describes what a match does in one line an operator reads.</summary>
    /// <returns>The changes in declared order, and the fact that a match ends the pass where it does.</returns>
    internal string DescribeActions()
    {
        var changes = this.Actions is { Count: > 0 } actions
            ? string.Join(", ", actions.Select(action => action.Describe()))
            : "nothing";

        return this.StopWhenMatched ? $"{changes}; ends the pass" : changes;
    }
}

/// <summary>One change a rule declares.</summary>
/// <param name="Position">Where the change sits in the order the rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change, named as the mutation it will be requested through.</param>
/// <param name="Destination">How a relocation or a copy names its folder — an alias, or a role as <c>role:Junk</c> — absent for every other change.</param>
/// <param name="DesiredSeenState">Which way a <c>\Seen</c> change was asked for, absent for every other change.</param>
internal sealed record LoadedRuleAction(
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("mutation")] string? Mutation,
    [property: JsonPropertyName("destination")] string? Destination,
    [property: JsonPropertyName("desiredSeenState")] bool? DesiredSeenState)
{
    /// <summary>Describes the change and the parameter it carries, where it carries one.</summary>
    /// <returns>One phrase, such as <c>Relocate → archive</c>.</returns>
    internal string Describe() => this switch
    {
        { Destination: { Length: > 0 } destination } => $"{this.Mutation} → {ConsoleSafeText.Sanitize(destination)}",
        { DesiredSeenState: { } isSeen } => $"{this.Mutation} → {(isSeen ? "read" : "unread")}",
        _ => this.Mutation ?? "an unreported change",
    };
}
