// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.Rules.Preview;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Api;

/// <summary>One mail account's stored rules, as the administrative endpoint serves them.</summary>
/// <param name="Account">The account the rules belong to.</param>
/// <param name="Version">The set's version, which a write states it read.</param>
/// <param name="Revision">The derived identity of the enabled rules, which every recorded execution names.</param>
/// <param name="Held">Whether no pass runs the set, because an enabled rule no longer compiles.</param>
/// <param name="UncompiledRules">The enabled rules that hold the set, by identifier, empty for a set that runs.</param>
/// <param name="Rules">Every stored rule, disabled ones included, in the order they are evaluated in.</param>
/// <remarks>
/// The order is the answer as much as the rules are, so nothing here sorts: a caller reading this is finding out which
/// rule reaches a message first and whether anything above it ends the pass.
/// </remarks>
internal sealed record MailRuleSetResponse(
    string Account,
    long Version,
    string Revision,
    bool Held,
    IReadOnlyList<Guid> UncompiledRules,
    IReadOnlyList<MailRuleResponse> Rules)
{
    /// <summary>Describes one account's stored set for the wire.</summary>
    /// <param name="reading">The stored set and the rules that hold it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reading" /> is <see langword="null" />.</exception>
    internal static MailRuleSetResponse For(MailRuleSetReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new MailRuleSetResponse(
            reading.Stored.Account.Value,
            reading.Stored.Version,
            reading.Stored.Revision.Value,
            reading.IsHeld,
            [.. reading.UncompiledRules.Select(rule => rule.Value)],
            [.. reading.Stored.Declarations.Select(MailRuleResponse.For)]);
    }
}

/// <summary>One stored rule, as the administrative endpoint serves it.</summary>
/// <param name="Id">The identifier the rule keeps through every rename and edit.</param>
/// <param name="Position">Where the rule sits in its account's order, counted from zero.</param>
/// <param name="Name">The name a person reads it under.</param>
/// <param name="Enabled">Whether a pass applies it.</param>
/// <param name="Condition">The condition as it was written.</param>
/// <param name="Actions">What a match asks the mailbox for, in the order the rule declares the changes.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass rather than continuing to the rules below.</param>
/// <param name="Triggers">The automatic triggers it takes part in, empty for a rule only a requested run applies.</param>
/// <param name="Schedule">The occasions the rule declares, in their canonical form, absent for a rule declaring none.</param>
internal sealed record MailRuleResponse(
    Guid Id,
    int Position,
    string Name,
    bool Enabled,
    string Condition,
    IReadOnlyList<MailRuleActionResponse> Actions,
    bool StopWhenMatched,
    IReadOnlyList<string> Triggers,
    string? Schedule)
{
    /// <summary>Describes one stored rule for the wire.</summary>
    /// <param name="rule">The stored rule.</param>
    /// <param name="position">Where it sits in its account's order.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule" /> is <see langword="null" />.</exception>
    internal static MailRuleResponse For(MailRuleDeclaration rule, int position)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new MailRuleResponse(
            rule.Id.Value,
            position,
            rule.Name,
            rule.Enabled,
            rule.ConditionText,
            [.. rule.Actions.Select((action, actionPosition) => MailRuleActionResponse.For(action, actionPosition))],
            rule.StopWhenMatched,
            [.. rule.Triggers.Select(trigger => trigger.Name)],
            rule.Schedule?.CanonicalForm);
    }
}

/// <summary>One change a rule declares, as the administrative endpoint serves it.</summary>
/// <param name="Position">Where the change sits in the order the rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change, named as the mutation it will be requested through.</param>
/// <param name="Destination">
/// How a relocation or a copy names its folder, absent for every other change. It is the text the rule wrote — an alias,
/// or a role as <c>role:Junk</c> — rather than the folder it resolves to, because what this serves is the declaration.
/// </param>
/// <param name="DesiredSeenState">Which way a <c>\Seen</c> change was asked for, absent for every other change.</param>
/// <param name="DesiredFlaggedState">Which way a <c>\Flagged</c> change was asked for, absent for every other change.</param>
/// <param name="Keywords">The keywords a keyword change names, absent for every other change and empty for a replacement clearing them all.</param>
internal sealed record MailRuleActionResponse(
    int Position,
    string Mutation,
    string? Destination,
    bool? DesiredSeenState,
    bool? DesiredFlaggedState,
    IReadOnlyList<string>? Keywords)
{
    /// <summary>Describes one declared change for the wire.</summary>
    /// <param name="action">The change the rule declares.</param>
    /// <param name="position">Where it sits in the rule's declared order.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
    internal static MailRuleActionResponse For(MailRuleAction action, int position)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new MailRuleActionResponse(
            position,
            action.Mutation.Name,
            action.Destination?.ToString(),
            action.DesiredSeenState,
            action.DesiredFlaggedState,
            action.Keywords is { } keywords ? [.. keywords.Values] : null);
    }
}

/// <summary>What a caller asks a whole-mailbox rule run for.</summary>
/// <param name="Account">The account to run the rules over, as this deployment's configuration names it.</param>
internal sealed record MailRuleRunRequest(string? Account);

/// <summary>What a caller asks of one change to an account's rules.</summary>
/// <param name="Account">The account whose rules change.</param>
/// <param name="Version">The version of the account's rules the change was written against, which a reading reports.</param>
/// <param name="Rule">The rule the change is about, by its identifier or its name within the account; absent for a create.</param>
/// <param name="Declaration">The rule object a create or a replacement writes; absent for every other change.</param>
/// <param name="Position">Where a move puts the rule, counted from zero; absent for every other change.</param>
/// <remarks>One shape for every change, so each route reads the fields it needs and refuses a request missing one rather than five shapes differing by a field.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailRuleChangeRequest(
    string? Account = null,
    long? Version = null,
    string? Rule = null,
    JsonElement? Declaration = null,
    int? Position = null);

/// <summary>What one change to an account's rules did.</summary>
/// <param name="Account">The account whose rules changed.</param>
/// <param name="Rule">The rule the change was about, which for a create is the identifier it was given.</param>
/// <param name="Name">The name the rule carries after the change, or carried before it was removed.</param>
/// <param name="Version">The version the account's rules are at now, which the next change states.</param>
/// <param name="Revision">The revision of the account's enabled rules now, which every later execution names.</param>
internal sealed record MailRuleChangeResponse(
    string Account,
    Guid Rule,
    string Name,
    long Version,
    string Revision);

/// <summary>What a caller asks one page of a preview for.</summary>
/// <param name="Account">The account whose stored mail the rule is evaluated over.</param>
/// <param name="Rule">A stored rule to preview, by its identifier or its name; absent when a candidate is carried.</param>
/// <param name="Declaration">A rule object not yet written, to preview instead of a stored rule.</param>
/// <param name="After">The message a previous page said to continue after, absent for the first page.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailRulePreviewPageRequest(
    string? Account = null,
    string? Rule = null,
    JsonElement? Declaration = null,
    Guid? After = null);

/// <summary>One page of a preview: which messages the rule would match, and what each would be asked for.</summary>
/// <param name="Account">The account whose mail was read.</param>
/// <param name="ExaminedEmailCount">How many messages the page evaluated the rule against.</param>
/// <param name="FailedEmailCount">How many of them the condition produced no answer for.</param>
/// <param name="Matches">The stored identities of the messages the rule matched, in the order the walk reached them.</param>
/// <param name="Requested">What a match would ask the mailbox for, which is the same for every message it matches.</param>
/// <param name="ContinueAfter">The message the next page continues after, absent when the account's mail is exhausted.</param>
/// <remarks>
/// Identities and counts, and nothing about what the messages say: a preview reports which mail a rule reaches, and the
/// mail itself is read where every other read of it is authorized.
/// </remarks>
internal sealed record MailRulePreviewResponse(
    string Account,
    int ExaminedEmailCount,
    int FailedEmailCount,
    IReadOnlyList<Guid> Matches,
    IReadOnlyList<MailRuleActionResponse> Requested,
    Guid? ContinueAfter)
{
    /// <summary>Describes one page of a preview for the wire.</summary>
    /// <param name="account">The account whose mail was read.</param>
    /// <param name="result">The page.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    internal static MailRulePreviewResponse For(MailAccountId account, MailRulePreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new MailRulePreviewResponse(
            account.Value,
            result.ExaminedEmailCount,
            result.FailedEmailCount,
            [.. result.Matches.Select(email => email.Value)],
            [.. result.Requested.Select((action, position) => MailRuleActionResponse.For(action, position))],
            result.ContinueAfter?.Value);
    }
}

/// <summary>What asking for a run did, as the administrative endpoint serves it.</summary>
/// <param name="Started">Whether this request is what put the run in front of the account.</param>
/// <param name="Run">The run the account now has outstanding, which is the one already under way when nothing started.</param>
/// <remarks>
/// The two are reported together because a caller that asked twice needs both: the run is the answer either way, and
/// whether this request started it is what tells "I have just begun a pass" from "one was already going".
/// </remarks>
internal sealed record MailRuleRunStartResponse(bool Started, MailRuleRunResponse Run);

/// <summary>Where an account's run stands, as the administrative endpoint serves it.</summary>
/// <param name="Account">The account the answer is about.</param>
/// <param name="Run">The run, or <see langword="null" /> where the account has never been asked for one.</param>
internal sealed record MailRuleRunStateResponse(string Account, MailRuleRunResponse? Run);

/// <summary>One whole-mailbox rule run, as the administrative endpoint serves it.</summary>
/// <param name="RequestedAt">When the run was asked for.</param>
/// <param name="Trigger">What started the run, which is what says whether anybody asked for it.</param>
/// <param name="Revision">The rule set the run is bound to, absent until the first pass picks it up.</param>
/// <param name="EvaluatedEmailCount">How many of the account's emails the run has evaluated.</param>
/// <param name="MatchedEmailCount">How many of those at least one rule matched.</param>
/// <param name="SkippedEmailCount">How many were stepped over because their body text had not been extracted yet.</param>
/// <param name="EndedAt">When the run stopped being outstanding, absent while it still is.</param>
/// <param name="Ending">How it ended, absent for exactly as long as <paramref name="EndedAt" /> is.</param>
/// <remarks>
/// Counts and instants, and no message is named. The progress a caller watches is how much of the mailbox has been
/// read, which the counts state without saying anything about what was in it.
/// </remarks>
internal sealed record MailRuleRunResponse(
    DateTimeOffset RequestedAt,
    string Trigger,
    string? Revision,
    int EvaluatedEmailCount,
    int MatchedEmailCount,
    int SkippedEmailCount,
    DateTimeOffset? EndedAt,
    string? Ending)
{
    /// <summary>Describes one run for the wire.</summary>
    /// <param name="run">The run as it stands.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    internal static MailRuleRunResponse For(MailRuleEvaluationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new MailRuleRunResponse(
            run.RequestedAt,
            run.Trigger.ToString(),
            run.Revision.IsSpecified ? run.Revision.Value : null,
            run.EvaluatedEmailCount,
            run.MatchedEmailCount,
            run.SkippedEmailCount,
            run.EndedAt,
            run.Ending?.ToString());
    }
}

/// <summary>One page of an account's rule history, as the administrative endpoint serves it.</summary>
/// <param name="Executions">The executions, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the history.</param>
internal sealed record MailRuleHistoryPageResponse(
    IReadOnlyList<MailRuleExecutionResponse> Executions,
    string? NextCursor)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page read from the history.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static MailRuleHistoryPageResponse For(MailRuleExecutionPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new MailRuleHistoryPageResponse(
            [.. page.Executions.Select(MailRuleExecutionResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>One recorded rule execution, as the administrative endpoint serves it.</summary>
/// <param name="Id">What addresses this execution.</param>
/// <param name="Email">The stable local identity of the message, which is the same one every other read names it by.</param>
/// <param name="Rule">The name the rule carried when it was evaluated.</param>
/// <param name="RuleId">The rule's identifier, which still names it after a rename, absent for an execution recorded before rules had one.</param>
/// <param name="Revision">The rule set revision the pass was bound to, which the condition is retrievable from.</param>
/// <param name="Trigger">Which of the pass's two walks reached the message.</param>
/// <param name="Outcome">What the condition concluded.</param>
/// <param name="ConditionFailure">Why it produced no answer, absent for every execution that produced one.</param>
/// <param name="ReadFacts">The facts the condition read, by name.</param>
/// <param name="Actions">The changes the rule declared and what became of each.</param>
/// <param name="EvaluatedAt">When the message was evaluated.</param>
/// <param name="Duration">How long the condition took to answer.</param>
/// <remarks>
/// <strong>The facts are names and never values.</strong> What the condition compared is retrievable from the recorded
/// revision, which the account's archive resolves to the rules it was taken over — so the reasoning is reconstructible
/// without a sender address, a subject, or a span of extracted text ever leaving the mailbox.
/// </remarks>
internal sealed record MailRuleExecutionResponse(
    Guid Id,
    Guid Email,
    string Rule,
    Guid? RuleId,
    string Revision,
    string Trigger,
    string Outcome,
    string? ConditionFailure,
    IReadOnlyList<string> ReadFacts,
    IReadOnlyList<MailRuleExecutedActionResponse> Actions,
    DateTimeOffset EvaluatedAt,
    TimeSpan Duration)
{
    /// <summary>Describes one execution for the wire.</summary>
    /// <param name="execution">The execution read from the history.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execution" /> is <see langword="null" />.</exception>
    internal static MailRuleExecutionResponse For(MailRuleExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        return new MailRuleExecutionResponse(
            execution.Id.Value,
            execution.StoredEmailId.Value,
            execution.RuleName,
            execution.RuleId?.Value,
            execution.Revision.Value,
            execution.Trigger.ToString(),
            execution.Outcome.ToString(),
            execution.ConditionFailure?.ToString(),
            [.. execution.ReadFacts.Select(fact => fact.Name)],
            [.. execution.Actions.Select(MailRuleExecutedActionResponse.For)],
            execution.EvaluatedAt,
            execution.Duration);
    }
}

/// <summary>One change a matching rule declared, as the administrative endpoint serves it.</summary>
/// <param name="Position">Where the change sits in the order the rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change asked for.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="Destination">The folder the change named, absent for a change naming none.</param>
/// <param name="FailureReason">Why nothing was recorded, present exactly for a change the recorder refused.</param>
/// <param name="MutationRecord">The record carrying the request, present exactly for a change that opened one.</param>
/// <remarks>
/// The record identifier is where what happened on the server is answered from. This says what the rule asked for and
/// nothing about how the mailbox took it, which is the mutation trail's to state and is not restated here.
/// </remarks>
internal sealed record MailRuleExecutedActionResponse(
    int Position,
    string Mutation,
    string Outcome,
    string? Destination,
    string? FailureReason,
    Guid? MutationRecord)
{
    /// <summary>Describes one recorded change for the wire.</summary>
    /// <param name="action">The change the execution recorded.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
    internal static MailRuleExecutedActionResponse For(MailRuleExecutedAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new MailRuleExecutedActionResponse(
            action.Position,
            action.Mutation.Name,
            action.Outcome.ToString(),
            action.Destination,
            action.FailureReason?.ToString(),
            action.MutationRecordId?.Value);
    }
}
