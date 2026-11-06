// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Rules;

namespace MailFathom.Host.Api;

/// <summary>One mail account's rules as a file: what an export writes and an import reads.</summary>
/// <param name="Rules">Every rule, disabled ones included, in the order a pass evaluates them in.</param>
/// <remarks>
/// <para>
/// Each rule is the rule object a create reads, with <c>Enabled</c> beside it, and the keys are written as the mail-rules
/// page writes them — which is also how the retired <c>MailRules:Rules</c> section wrote them, so a rule exported here and
/// a rule a configuration declared read the same way.
/// </para>
/// <para>
/// It carries the rules and nothing else: no account, no identifier, no version, and no secret. A rule's identity is the
/// deployment's to mint, so the same file brought into another account or another deployment is matched there by name.
/// </para>
/// </remarks>
internal sealed record MailRuleSetDocument(
    [property: JsonPropertyName("Rules")] IReadOnlyList<MailRuleDocumentEntry> Rules)
{
    /// <summary>Writes an account's stored rules as the file.</summary>
    /// <param name="declarations">The stored rules, in order.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarations" /> is <see langword="null" />.</exception>
    internal static MailRuleSetDocument For(IReadOnlyList<MailRuleDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        return new MailRuleSetDocument([.. declarations.Select(MailRuleDocumentEntry.For)]);
    }
}

/// <summary>One rule of the file: the rule object, and whether the rule ran where it was exported from.</summary>
/// <param name="Name">The rule's name.</param>
/// <param name="Enabled">Whether a pass applied the rule where it was exported from.</param>
/// <param name="Condition">The condition, as written.</param>
/// <param name="Triggers">The automatic triggers it takes part in, by name.</param>
/// <param name="Schedule">
/// The occasions a scheduled walk happens on, written as a schedule is declared — <c>Every …</c> or <c>Daily at …</c> —
/// rather than in the canonical form a revision is derived from, which nothing reads back; absent for a rule declaring none.
/// </param>
/// <param name="Actions">What a match asks the mailbox for, one key per kind of change.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass.</param>
internal sealed record MailRuleDocumentEntry(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Enabled")] bool Enabled,
    [property: JsonPropertyName("Condition")] string Condition,
    [property: JsonPropertyName("Triggers")] IReadOnlyList<string> Triggers,
    [property: JsonPropertyName("Schedule"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Schedule,
    [property: JsonPropertyName("Actions")] MailRuleActionsRequest Actions,
    [property: JsonPropertyName("StopWhenMatched")] bool StopWhenMatched)
{
    /// <summary>Writes one stored rule as an entry of the file.</summary>
    /// <param name="rule">The stored rule.</param>
    /// <returns>The entry.</returns>
    internal static MailRuleDocumentEntry For(MailRuleDeclaration rule) => new(
        rule.Name,
        rule.Enabled,
        rule.ConditionText,
        [.. rule.Triggers.Select(trigger => trigger.Name)],
        rule.Schedule?.Declaration,
        MailRuleActionsRequest.For(rule.Actions),
        rule.StopWhenMatched);
}
