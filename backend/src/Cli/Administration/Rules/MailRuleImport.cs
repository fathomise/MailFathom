// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Rules;

/// <summary>One mail account's rules as the deployment exports them: the file an import reads.</summary>
/// <param name="Rules">Every rule, as a rule object with <c>Enabled</c> beside it, in the order they run.</param>
/// <remarks>The rules are kept as the deployment wrote them, so what reaches the file is exactly what it sent.</remarks>
internal sealed record ExportedRuleSet(
    [property: JsonPropertyName("Rules")] IReadOnlyList<JsonElement>? Rules);

/// <summary>What an import, or a preview of one, sends.</summary>
/// <param name="Account">The account whose rules the import changes.</param>
/// <param name="Version">The version of the account's rules the import was decided against.</param>
/// <param name="Mode"><c>Replace</c> or <c>Merge</c>.</param>
/// <param name="Rules">The rules, as rule objects, in the order they are imported in.</param>
internal sealed record MailRuleImportRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("rules")] IReadOnlyList<JsonElement> Rules);

/// <summary>What an import did, or would do, as the deployment reports it.</summary>
/// <param name="Account">The account whose rules the import changes.</param>
/// <param name="Version">The version the rules are at now.</param>
/// <param name="Revision">The revision of the account's enabled rules now.</param>
/// <param name="Committed">Whether the import was written.</param>
/// <param name="Rules">What the import does to each rule: the imported ones in their order, then the ones a replacement removes.</param>
internal sealed record MailRuleImport(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("revision")] string? Revision,
    [property: JsonPropertyName("committed")] bool Committed,
    [property: JsonPropertyName("rules")] IReadOnlyList<MailRuleImportedRule>? Rules);

/// <summary>What an import does to one rule.</summary>
/// <param name="Rule">The rule's identifier, absent for a rule a preview would add.</param>
/// <param name="Name">The rule's name.</param>
/// <param name="Effect"><c>Added</c>, <c>Changed</c>, <c>Moved</c>, <c>Unchanged</c>, or <c>Removed</c>.</param>
/// <param name="Enabled">Whether a pass applies the rule afterwards, or applied it before it was removed.</param>
internal sealed record MailRuleImportedRule(
    [property: JsonPropertyName("rule")] Guid? Rule,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("effect")] string? Effect,
    [property: JsonPropertyName("enabled")] bool Enabled);
