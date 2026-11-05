// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Rules;

/// <summary>What a change to one account's rules sends.</summary>
/// <param name="Account">The account whose rules change.</param>
/// <param name="Version">The version of the account's rules the change was written against.</param>
/// <param name="Rule">The rule the change is about, by its identifier or its name; absent for a create.</param>
/// <param name="Declaration">The rule object a create or a replacement writes, as the operator wrote it.</param>
/// <param name="Position">Where a move puts the rule; absent for every other change.</param>
internal sealed record MailRuleChangeRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("rule")] string? Rule = null,
    [property: JsonPropertyName("declaration")] JsonElement? Declaration = null,
    [property: JsonPropertyName("position")] int? Position = null);

/// <summary>What a change to one account's rules did, as the deployment reports it.</summary>
/// <param name="Account">The account whose rules changed.</param>
/// <param name="Rule">The rule the change was about, which for a create is the identifier it was given.</param>
/// <param name="Name">The name the rule carries after the change, or carried before it was removed.</param>
/// <param name="Version">The version the account's rules are at now.</param>
/// <param name="Revision">The revision of the account's enabled rules now.</param>
internal sealed record MailRuleChange(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("rule")] Guid Rule,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("revision")] string? Revision);

/// <summary>What one page of a preview asks for.</summary>
/// <param name="Account">The account whose stored mail the rule is evaluated over.</param>
/// <param name="Rule">A stored rule, by its identifier or its name; absent when a candidate is carried.</param>
/// <param name="Declaration">A rule object not yet written; absent when a stored rule is named.</param>
/// <param name="After">The message a previous page said to continue after.</param>
internal sealed record MailRulePreviewPageRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("rule")] string? Rule,
    [property: JsonPropertyName("declaration")] JsonElement? Declaration,
    [property: JsonPropertyName("after")] Guid? After);

/// <summary>One page of a preview, as the deployment reports it.</summary>
/// <param name="Account">The account whose mail was read.</param>
/// <param name="ExaminedEmailCount">How many messages the page evaluated the rule against.</param>
/// <param name="FailedEmailCount">How many of them the condition produced no answer for.</param>
/// <param name="Matches">The stored identities of the messages the rule matched.</param>
/// <param name="Requested">What a match would ask the mailbox for.</param>
/// <param name="ContinueAfter">The message the next page continues after, absent when the mail is exhausted.</param>
internal sealed record MailRulePreviewPage(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("examinedEmailCount")] int ExaminedEmailCount,
    [property: JsonPropertyName("failedEmailCount")] int FailedEmailCount,
    [property: JsonPropertyName("matches")] IReadOnlyList<Guid>? Matches,
    [property: JsonPropertyName("requested")] IReadOnlyList<LoadedRuleAction>? Requested,
    [property: JsonPropertyName("continueAfter")] Guid? ContinueAfter);
