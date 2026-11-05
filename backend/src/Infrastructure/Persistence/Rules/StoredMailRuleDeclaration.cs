// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;

namespace MailFathom.Infrastructure.Persistence.Rules;

/// <summary>One rule as the archive keeps it, which is every part of its declaration in a form JSON can carry.</summary>
/// <param name="Id">The rule's identifier.</param>
/// <param name="Name">The name it carried.</param>
/// <param name="Enabled">Whether it ran.</param>
/// <param name="Condition">The condition as written.</param>
/// <param name="Actions">What a match did.</param>
/// <param name="StopWhenMatched">Whether a match ended the pass.</param>
/// <param name="Triggers">The names of its triggers.</param>
/// <param name="Schedule">Its schedule, as a declaration the recurrence reads back.</param>
internal sealed record StoredMailRuleDeclaration(
    Guid Id,
    string Name,
    bool Enabled,
    string Condition,
    IReadOnlyList<StoredMailRuleAction> Actions,
    bool StopWhenMatched,
    IReadOnlyList<string> Triggers,
    string? Schedule)
{
    /// <summary>Serializes a whole set's declarations into the archive's document.</summary>
    /// <param name="declarations">The declarations, in position order.</param>
    /// <returns>The JSON array.</returns>
    public static string SerializeSet(IReadOnlyList<MailRuleDeclaration> declarations) =>
        JsonSerializer.Serialize(
            [.. declarations.Select(From)],
            StoredMailRuleJsonContext.Default.IReadOnlyListStoredMailRuleDeclaration);

    /// <summary>Reads a whole set's declarations back out of the archive's document.</summary>
    /// <param name="document">The JSON array.</param>
    /// <returns>The declarations, in position order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the document is not one this store wrote.</exception>
    public static IReadOnlyList<MailRuleDeclaration> DeserializeSet(string document) =>
        [.. (JsonSerializer.Deserialize(document, StoredMailRuleJsonContext.Default.IReadOnlyListStoredMailRuleDeclaration)
            ?? throw new InvalidOperationException("An archived rule set holds no declarations."))
            .Select(stored => stored.ToDeclaration())];

    /// <summary>Serializes one rule's actions into the rule row's document.</summary>
    /// <param name="actions">The actions, in declared order.</param>
    /// <returns>The JSON array.</returns>
    public static string SerializeActions(IReadOnlyList<MailRuleAction> actions) =>
        JsonSerializer.Serialize(
            [.. actions.Select(StoredMailRuleAction.From)],
            StoredMailRuleJsonContext.Default.IReadOnlyListStoredMailRuleAction);

    /// <summary>Reads one rule's actions back out of the rule row's document.</summary>
    /// <param name="document">The JSON array.</param>
    /// <returns>The actions, in declared order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the document is not one this store wrote.</exception>
    public static IReadOnlyList<MailRuleAction> DeserializeActions(string document) =>
        [.. (JsonSerializer.Deserialize(document, StoredMailRuleJsonContext.Default.IReadOnlyListStoredMailRuleAction)
            ?? throw new InvalidOperationException("A stored rule holds no action list."))
            .Select(stored => stored.ToAction())];

    /// <summary>Reads the triggers a row or a document names.</summary>
    /// <param name="names">The trigger names.</param>
    /// <returns>The triggers.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a name is one this build does not know.</exception>
    public static IReadOnlyList<MailRuleTrigger> ReadTriggers(IEnumerable<string> names) =>
    [
        .. names.Select(name => MailRuleTrigger.TryParseName(name, out var trigger)
            ? trigger
            : throw new InvalidOperationException("A stored rule names a trigger this build does not know.")),
    ];

    /// <summary>Reads the schedule a row or a document names.</summary>
    /// <param name="declaration">The schedule's declaration, or <see langword="null" />.</param>
    /// <returns>The recurrence, or <see langword="null" />.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the declaration is one this build cannot read.</exception>
    public static JobRecurrence? ReadSchedule(string? declaration) =>
        declaration is null
            ? null
            : JobRecurrence.TryParse(declaration, out var recurrence, out _)
                ? recurrence
                : throw new InvalidOperationException("A stored rule names a schedule this build cannot read.");

    private static StoredMailRuleDeclaration From(MailRuleDeclaration declaration) => new(
        declaration.Id.Value,
        declaration.Name,
        declaration.Enabled,
        declaration.ConditionText,
        [.. declaration.Actions.Select(StoredMailRuleAction.From)],
        declaration.StopWhenMatched,
        [.. declaration.Triggers.Select(trigger => trigger.Name)],
        declaration.Schedule?.Declaration);

    private MailRuleDeclaration ToDeclaration() => new(
        MailRuleId.Create(this.Id),
        this.Name,
        this.Enabled,
        this.Condition,
        [.. this.Actions.Select(action => action.ToAction())],
        this.StopWhenMatched,
        ReadTriggers(this.Triggers),
        ReadSchedule(this.Schedule));
}
