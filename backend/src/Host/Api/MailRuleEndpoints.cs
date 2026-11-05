// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Editing;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the three things an operator reads or asks of one mail account's rules.</summary>
/// <remarks>
/// <para>
/// Reading the account's stored rules, asking for them to be run over its whole mailbox, and reading what they did.
/// Each mail account's rules are kept in PostgreSQL under
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0048-keeping-a-mail-accounts-rules-in-postgresql-and-changing-them-at-runtime.md">ADR 0048</see>,
/// and every write to them goes through <see cref="MailRuleSetEditor" />; no route here writes one yet.
/// </para>
/// <para>
/// They are here rather than on the MCP surface because none of them is anything a model reasons over, and because what
/// bounds administrative access is what should bound the ability to start a pass over a whole mailbox. The three grants
/// differ accordingly: reading the stored set and a run's progress is <c>mailfathom.admin.read</c>, asking for a pass is
/// <c>mailfathom.admin.operate</c>, and the history is <c>mailfathom.admin.audit.read</c>, because what a rule concluded
/// about somebody's mail is derived from that mail rather than a report of this deployment's own state.
/// </para>
/// <para>
/// Nothing any of them answers with is mail. Rule names, conditions, folder aliases, mutation names, fact names, counts,
/// instants, and identifiers are the whole of it. A condition is authored text that can name a correspondent, which is
/// why reading the set needs the read grant over the account rather than over the deployment.
/// </para>
/// </remarks>
internal static class MailRuleEndpoints
{
    /// <summary>The route one account's stored rules are read from, relative to the administrative prefix.</summary>
    internal const string RulesRoute = "/rules";

    /// <summary>The route a whole-mailbox run is asked for and read from, relative to the administrative prefix.</summary>
    /// <remarks>
    /// One path and two verbs, because they are one operation performed and then watched: what the reading reports is
    /// the run the write asked for, and an operator who started a pass over their mailbox comes back to the same place
    /// to find out where it got to.
    /// </remarks>
    internal const string RunsRoute = "/rules/runs";

    /// <summary>The route the recorded history is read from, relative to the administrative prefix.</summary>
    internal const string HistoryRoute = "/rules/history";

    /// <summary>The greatest request body the run route reads before refusing it.</summary>
    /// <remarks>
    /// The body names one account, so a few hundred bytes is the whole of anything it could mean. Stated because the
    /// server's own default is measured in tens of megabytes, which for this route would let an authenticated client
    /// make the process buffer a body four orders of magnitude larger than the request it is sending.
    /// </remarks>
    internal const int MaxRunRequestBytes = 4 * 1024;

    /// <summary>Maps the rule routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailRules(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(RulesRoute, ReadRulesAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the write route beside it
        // reaches it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body
        // feature, so a body over the bound is answered 413 before the handler is reached.
        api.MapPost(RunsRoute, StartRunAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRunRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminOperate);

        api.MapGet(RunsRoute, ReadRunAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        api.MapGet(HistoryRoute, ReadHistoryAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminAuditRead);
    }

    /// <summary>Reports one account's stored rules, the set's version, its revision, and whether it is held.</summary>
    /// <param name="account">The identifier the deployment generated for the account whose rules are read.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="ruleSets">Reads the account's stored set.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account the request names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the set, including an account that has no rules, or <c>400</c>.</returns>
    /// <remarks>
    /// The whole set rather than one rule, because a rule is only meaningful in the order it sits in: which rule is
    /// reached first is a property of the set, and a caller reading one rule alone could not tell whether anything above
    /// it ends the pass. Showing one rule is therefore a matter of choosing from this answer rather than of asking a
    /// different question.
    /// </remarks>
    internal static async Task<Results<Ok<MailRuleSetResponse>, ProblemHttpResult>> ReadRulesAsync(
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetReader ruleSets,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(ruleSets);

        if (await AdminAccountRequest.ResolveCoveredAsync(account, MailFathomPermission.AdminRead, accounts, authorization, cancellationToken) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        return TypedResults.Ok(MailRuleSetResponse.For(await ruleSets.ReadAsync(servedAccount, cancellationToken)));
    }

    /// <summary>Asks for one account's rules to be run over every message stored for it.</summary>
    /// <param name="request">The account the run is asked for.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="requests">Records the request, or reports the run already in front of the account.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account the request names.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the run, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// It records that the run is wanted and evaluates nothing. The pass is a step of the account's synchronization run,
    /// so the request neither performs the work nor keeps it alive — which is what stops an operator's terminal closing
    /// from cancelling a walk of their mailbox, and what makes this answer immediately however large the mailbox is.
    /// <para>
    /// A second request while one is outstanding is answered with the run already under way rather than refused. Asking
    /// twice for the same thing is asking once: what the caller wanted is for the mail to be re-evaluated, and it is
    /// going to be. The answer says which of the two happened.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<MailRuleRunStartResponse>, ProblemHttpResult>> StartRunAsync(
        [FromBody] MailRuleRunRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleEvaluationRunRequests requests,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(requests);

        if (await AdminAccountRequest.ResolveCoveredAsync(request?.Account, MailFathomPermission.AdminOperate, accounts, authorization, cancellationToken) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(request?.Account);
        }

        var submitted = await requests.SubmitAsync(servedAccount, cancellationToken);

        return TypedResults.Ok(new MailRuleRunStartResponse(
            submitted.Accepted,
            MailRuleRunResponse.For(submitted.Run)));
    }

    /// <summary>Reports where one account's whole-mailbox run has got to, or how the last one ended.</summary>
    /// <param name="account">The identifier the deployment generated for the account whose run is read.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="runs">Reads the one run an account may have outstanding, or the ending of the last one.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account the request names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the run, <c>200</c> with none where the account has never been asked for one, or <c>400</c>.</returns>
    /// <remarks>
    /// An account that has never been asked for a run is an outcome rather than a refusal, so the answer carries no run
    /// instead of a <c>404</c>: the caller asked a question this deployment can answer, and the answer is that nothing
    /// has been asked for.
    /// </remarks>
    internal static async Task<Results<Ok<MailRuleRunStateResponse>, ProblemHttpResult>> ReadRunAsync(
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleEvaluationRunReader runs,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(runs);

        if (await AdminAccountRequest.ResolveCoveredAsync(account, MailFathomPermission.AdminRead, accounts, authorization, cancellationToken) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        var run = await runs.FindLatestAsync(servedAccount, cancellationToken);

        return TypedResults.Ok(new MailRuleRunStateResponse(
            servedAccount.Value,
            run is null ? null : MailRuleRunResponse.For(run)));
    }

    /// <summary>Serves one page of an account's rule history, or reports what was wrong with the request.</summary>
    /// <param name="account">The identifier the deployment generated for the account whose history is read.</param>
    /// <param name="rule">The rule to narrow to, or <see langword="null" /> for every rule.</param>
    /// <param name="email">The local identity of the message to narrow to, or <see langword="null" /> for every message.</param>
    /// <param name="from">The earliest evaluation instant served, inclusive, or <see langword="null" /> for none.</param>
    /// <param name="before">The evaluation instant to stop before, exclusive, or <see langword="null" /> for none.</param>
    /// <param name="pageSize">How many executions the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="history">Reads the page.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the named account.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c>, including an account this deployment does not configure, which mirrors the two audit
    /// trails beside it: an unknown account is a mistake in the request the caller wrote rather than a missing resource,
    /// and <c>404</c> is already what a client reads as "this port serves no administrative endpoint".
    /// </remarks>
    internal static async Task<Results<Ok<MailRuleHistoryPageResponse>, ProblemHttpResult>> ReadHistoryAsync(
        [FromQuery] string? account,
        [FromQuery] string? rule,
        [FromQuery] Guid? email,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleHistory history,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(history);

        if (await AdminAccountRequest.ResolveCoveredAsync(
                account,
                MailFathomPermission.AdminAuditRead,
                accounts,
                authorization,
                cancellationToken) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        MailRuleExecutionCursor? decodedCursor = null;

        if (cursor is not null && !MailRuleExecutionCursor.TryDecode(cursor, out decodedCursor))
        {
            return TypedResults.Problem(
                "The continuation cursor is not one this deployment issued.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var queryResult = MailRuleExecutionQuery.Create(
            servedAccount,
            rule,
            email is { } emailId ? StoredEmailId.Create(emailId) : null,
            from,
            before,
            pageSize,
            decodedCursor);

        if (queryResult.Query is not { } query)
        {
            return TypedResults.Problem(
                DescribeRefusal(queryResult.Outcome),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var page = await history.ReadPageAsync(query, cancellationToken);

        return TypedResults.Ok(MailRuleHistoryPageResponse.For(page));
    }

    /// <summary>States what a caller has to change, without restating the filters they already sent.</summary>
    private static string DescribeRefusal(MailRuleExecutionQueryOutcome outcome) => outcome switch
    {
        MailRuleExecutionQueryOutcome.PageSizeOutOfRange =>
            $"A rule history page holds between 1 and {MailRuleExecutionQuery.MaximumPageSize} executions.",
        MailRuleExecutionQueryOutcome.TimeRangeEmpty =>
            "The rule history time range ends at or before it begins, so it names no executions.",
        MailRuleExecutionQueryOutcome.RuleNameBlank =>
            "The rule filter names no rule. Leave it out to read every rule of the account.",
        _ => "The continuation cursor was issued for a different set of rule history filters.",
    };
}
