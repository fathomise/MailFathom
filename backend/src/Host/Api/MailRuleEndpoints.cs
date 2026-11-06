// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Editing;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.Rules.Preview;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves what an operator reads, asks, and changes of one mail account's rules.</summary>
/// <remarks>
/// <para>
/// Reading the account's stored rules, exporting and importing them as a file, changing them, previewing one over the account's mail, asking for them to be run
/// over its whole mailbox, and reading what they did. Each mail account's rules are kept in PostgreSQL under
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0048-keeping-a-mail-accounts-rules-in-postgresql-and-changing-them-at-runtime.md">ADR 0048</see>,
/// and every change goes through <see cref="MailRuleSetEditor" />: a change states the version it was written against,
/// a create always commits the rule disabled, and a rule is named by its identifier or by its name, which the editor
/// resolves against the set it has locked.
/// </para>
/// <para>
/// They are here rather than on the MCP surface because none of them is anything a model reasons over, and because what
/// bounds administrative access is what should bound the ability to start a pass over a whole mailbox. The grants
/// differ accordingly: reading the stored set, exporting it, previewing an import, and reading a run's progress is
/// <c>mailfathom.admin.read</c>, changing the set — an import included — is <c>mailfathom.admin.configuration.write</c>, asking for a pass is <c>mailfathom.admin.operate</c>, and the history
/// and a preview are <c>mailfathom.admin.audit.read</c>, because what a rule concluded or would conclude about somebody's
/// mail is derived from that mail rather than a report of this deployment's own state.
/// </para>
/// <para>
/// Nothing any of them answers with is mail. Rule names, conditions, folder aliases, mutation names, fact names, counts,
/// instants, and identifiers are the whole of it. A condition is authored text that can name a correspondent, which is
/// why reading the set needs the read grant over the account rather than over the deployment, and why a refused change
/// names each defective rule by its identifier rather than quoting what it declares.
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

    /// <summary>The route a rule replaces what it declares at, relative to the administrative prefix.</summary>
    /// <remarks>A create is a post to <see cref="RulesRoute" />, where the set is read; every other change is a path of its own beneath it.</remarks>
    internal const string ReplacementRoute = "/rules/replacement";

    /// <summary>The route a rule is enabled at, relative to the administrative prefix.</summary>
    /// <remarks>
    /// Enabling and disabling are two paths rather than one carrying a switch, because they are opposite decisions and
    /// one of them lets a rule start deleting mail: a mistyped value in a body would be the difference between the two.
    /// </remarks>
    internal const string EnablementRoute = "/rules/enablement";

    /// <summary>The route a rule is disabled at, relative to the administrative prefix.</summary>
    internal const string DisablementRoute = "/rules/disablement";

    /// <summary>The route a rule is moved to another position at, relative to the administrative prefix.</summary>
    internal const string PositionRoute = "/rules/position";

    /// <summary>The route a rule is removed at, relative to the administrative prefix.</summary>
    internal const string RemovalRoute = "/rules/removal";

    /// <summary>The route a rule is previewed at, relative to the administrative prefix.</summary>
    /// <remarks>A post although it changes nothing, because a candidate rule is a body and a condition in a query string would reach every access log on the way.</remarks>
    internal const string PreviewRoute = "/rules/preview";

    /// <summary>The route one account's rules are read from as a file, relative to the administrative prefix.</summary>
    internal const string ExportRoute = "/rules/export";

    /// <summary>The route a whole set of rules is imported into one account at, relative to the administrative prefix.</summary>
    internal const string ImportRoute = "/rules/import";

    /// <summary>The route an import is previewed at, relative to the administrative prefix.</summary>
    /// <remarks>A path of its own rather than a switch on the import, because one of the two writes and a mistyped value would be the difference between them.</remarks>
    internal const string ImportPreviewRoute = "/rules/import/preview";

    /// <summary>The greatest request body a change or a preview reads before refusing it.</summary>
    /// <remarks>
    /// The bound ADR 0048 sets for every rule write. One rule at its largest is a few kilobytes at the default condition
    /// length, so the bound is reached only by a body no rule could need, and it is refused before it is read.
    /// </remarks>
    internal const int MaxChangeRequestBytes = 1024 * 1024;

    private const string NoRuleNamed = "The change names no rule. Name it by its identifier or by its name within the account.";

    private const string NoSuchRule = "This account has no rule with that identifier or name.";

    private const string NoVersionStated = "A change states the version of the account's rules it was written against, which reading the rules reports.";

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

        MapChange(api.MapPost(RulesRoute, CreateRuleAsync));
        MapChange(api.MapPost(ReplacementRoute, ReplaceRuleAsync));
        MapChange(api.MapPost(EnablementRoute, EnableRuleAsync));
        MapChange(api.MapPost(DisablementRoute, DisableRuleAsync));
        MapChange(api.MapPost(PositionRoute, MoveRuleAsync));
        MapChange(api.MapPost(RemovalRoute, RemoveRuleAsync));
        MapChange(api.MapPost(ImportRoute, ImportRulesAsync));

        api.MapGet(ExportRoute, ExportRulesAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        // The read grant, because a preview of an import reports the account's stored rules and the verdict on a
        // candidate the caller wrote, and reads no mail.
        api.MapPost(ImportPreviewRoute, PreviewImportAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxChangeRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        // The audit grant rather than the read grant, because a preview names the messages a rule reaches, which is what
        // the history beside it is read under.
        api.MapPost(PreviewRoute, PreviewRuleAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxChangeRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminAuditRead);
    }

    /// <summary>Creates a rule at the end of an account's set, disabled.</summary>
    /// <param name="request">The account, the version read, and the rule object.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="editor">Writes the account's rules.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with what changed, <c>409</c> for a stale version, or <c>400</c>.</returns>
    internal static async Task<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>> CreateRuleAsync(
        [FromBody] MailRuleChangeRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetEditor editor,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (await ResolveWriteAsync(request, accounts, authorization, cancellationToken) is not { } write)
        {
            return RefuseWrite(request);
        }

        if (MailRuleDeclarationRequest.Read(request!.Declaration, out var refusal) is not { } draft)
        {
            return Refused(refusal!);
        }

        return Answered(write, await editor.CreateAsync(write, draft, cancellationToken));
    }

    /// <summary>Replaces what one rule declares, keeping its identifier, its position, and whether it runs.</summary>
    /// <param name="request">The account, the version read, the rule, and the rule object replacing it.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="editor">Writes the account's rules.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with what changed, <c>409</c> for a stale version, or <c>400</c>.</returns>
    internal static async Task<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>> ReplaceRuleAsync(
        [FromBody] MailRuleChangeRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetEditor editor,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (await ResolveWriteAsync(request, accounts, authorization, cancellationToken) is not { } write)
        {
            return RefuseWrite(request);
        }

        if (!MailRuleReference.TryCreate(request!.Rule, out var rule))
        {
            return Refused(NoRuleNamed);
        }

        if (MailRuleDeclarationRequest.Read(request.Declaration, out var refusal) is not { } draft)
        {
            return Refused(refusal!);
        }

        return Answered(write, await editor.ReplaceAsync(write, rule, draft, cancellationToken));
    }

    /// <summary>Enables one rule, so the next pass applies it.</summary>
    /// <param name="request">The account, the version read, and the rule.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="editor">Writes the account's rules.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with what changed, <c>409</c> for a stale version, or <c>400</c>.</returns>
    internal static Task<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>> EnableRuleAsync(
        [FromBody] MailRuleChangeRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetEditor editor,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken) =>
        ChangeNamedRuleAsync(
            request,
            accounts,
            authorization,
            (write, rule) => editor.SetEnabledAsync(write, rule, enabled: true, cancellationToken),
            cancellationToken);

    /// <summary>Disables one rule, so no pass applies it while it is kept.</summary>
    /// <param name="request">The account, the version read, and the rule.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="editor">Writes the account's rules.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with what changed, <c>409</c> for a stale version, or <c>400</c>.</returns>
    internal static Task<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>> DisableRuleAsync(
        [FromBody] MailRuleChangeRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetEditor editor,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken) =>
        ChangeNamedRuleAsync(
            request,
            accounts,
            authorization,
            (write, rule) => editor.SetEnabledAsync(write, rule, enabled: false, cancellationToken),
            cancellationToken);

    /// <summary>Moves one rule to another position in the order its account's set is evaluated in.</summary>
    /// <param name="request">The account, the version read, the rule, and its new position.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="editor">Writes the account's rules.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with what changed, <c>409</c> for a stale version, or <c>400</c>.</returns>
    internal static Task<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>> MoveRuleAsync(
        [FromBody] MailRuleChangeRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetEditor editor,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken) =>
        request?.Position is not { } position
            ? Task.FromResult<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>>(
                Refused("A move states the position the rule goes to, counted from 0 for the first rule evaluated."))
            : ChangeNamedRuleAsync(
                request,
                accounts,
                authorization,
                (write, rule) => editor.MoveAsync(write, rule, position, cancellationToken),
                cancellationToken);

    /// <summary>Removes one rule from its account's set.</summary>
    /// <param name="request">The account, the version read, and the rule.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="editor">Writes the account's rules.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with what changed, <c>409</c> for a stale version, or <c>400</c>.</returns>
    internal static Task<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>> RemoveRuleAsync(
        [FromBody] MailRuleChangeRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetEditor editor,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken) =>
        ChangeNamedRuleAsync(
            request,
            accounts,
            authorization,
            (write, rule) => editor.RemoveAsync(write, rule, cancellationToken),
            cancellationToken);

    /// <summary>Evaluates one rule over a page of an account's stored mail, changing nothing.</summary>
    /// <param name="request">The account, the stored rule or the candidate, and where the page continues from.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="preview">Evaluates the rule.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account.</param>
    /// <param name="cancellationToken">Cancels the preview.</param>
    /// <returns><c>200</c> with the page, or <c>400</c>.</returns>
    internal static async Task<Results<Ok<MailRulePreviewResponse>, ProblemHttpResult>> PreviewRuleAsync(
        [FromBody] MailRulePreviewPageRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRulePreview preview,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (await AdminAccountRequest.ResolveCoveredAsync(request?.Account, MailFathomPermission.AdminAuditRead, accounts, authorization, cancellationToken) is not { } account)
        {
            return AdminAccountRequest.Refuse(request?.Account);
        }

        var after = request!.After is { } continuation && continuation != Guid.Empty ? StoredEmailId.Create(continuation) : (StoredEmailId?)null;
        var names = MailRuleReference.TryCreate(request.Rule, out var rule);
        var carries = request.Declaration is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };

        if (names == carries)
        {
            return Refused("A preview names one stored rule, by its identifier or its name, or carries one rule object, and not both.");
        }

        MailRulePreviewRequest previewed;

        if (names)
        {
            previewed = MailRulePreviewRequest.OfStoredRule(account, rule, after);
        }
        else if (MailRuleDeclarationRequest.Read(request.Declaration, out var refusal) is { } draft)
        {
            previewed = MailRulePreviewRequest.OfCandidate(account, draft, after);
        }
        else
        {
            return Refused(refusal!);
        }

        var result = await preview.PreviewAsync(previewed, cancellationToken);

        return result.Outcome switch
        {
            MailRulePreviewOutcome.Previewed => TypedResults.Ok(MailRulePreviewResponse.For(account, result)),
            MailRulePreviewOutcome.RuleNotFound => Refused(NoSuchRule),
            _ => Refused(DescribeErrors("The rule was refused, so nothing was evaluated:", result.Errors)),
        };
    }

    /// <summary>Gives a change route its body bound and its grant, which every change shares.</summary>
    /// <remarks>
    /// The attribute is reached for its metadata rather than as an MVC filter: it implements
    /// <c>IRequestSizeLimitMetadata</c>, which the routing pipeline applies to the request body feature.
    /// </remarks>
    private static void MapChange(RouteHandlerBuilder route) => route
        .WithMetadata(new RequestSizeLimitAttribute(MaxChangeRequestBytes))
        .RequirePermissionOverTarget(MailFathomPermission.AdminConfigurationWrite);

    private static async Task<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>> ChangeNamedRuleAsync(
        MailRuleChangeRequest? request,
        IDeploymentMailAccountCatalog accounts,
        AccessAuthorization authorization,
        Func<MailRuleSetWrite, MailRuleReference, Task<MailRuleSetChangeResult>> change,
        CancellationToken cancellationToken)
    {
        if (await ResolveWriteAsync(request, accounts, authorization, cancellationToken) is not { } write)
        {
            return RefuseWrite(request);
        }

        return MailRuleReference.TryCreate(request!.Rule, out var rule)
            ? Answered(write, await change(write, rule))
            : Refused(NoRuleNamed);
    }

    /// <summary>Reads the account and the version a change states, or nothing where either is missing or the account is not served.</summary>
    /// <remarks>An account outside the caller's scope is answered as one this deployment does not serve. A request missing its version, or a move missing its position, is refused for that alone whatever account it names, so neither refusal depends on the account.</remarks>
    private static async Task<MailRuleSetWrite?> ResolveWriteAsync(
        MailRuleChangeRequest? request,
        IDeploymentMailAccountCatalog accounts,
        AccessAuthorization authorization,
        CancellationToken cancellationToken) =>
        await AdminAccountRequest.ResolveCoveredAsync(
                request?.Account,
                MailFathomPermission.AdminConfigurationWrite,
                accounts,
                authorization,
                cancellationToken) is { } account
            && request!.Version is { } version and >= 0
                ? new MailRuleSetWrite(account, version)
                : null;

    private static ProblemHttpResult RefuseWrite(MailRuleChangeRequest? request) =>
        string.IsNullOrWhiteSpace(request?.Account) || request.Version is >= 0
            ? AdminAccountRequest.Refuse(request?.Account)
            : Refused(NoVersionStated);

    private static Results<Ok<MailRuleChangeResponse>, ProblemHttpResult> Answered(MailRuleSetWrite write, MailRuleSetChangeResult result) =>
        result.Outcome switch
        {
            MailRuleSetChangeOutcome.Committed => TypedResults.Ok(new MailRuleChangeResponse(
                write.Account.Value,
                result.Rule.Value,
                result.Name,
                result.Version,
                result.Revision.Value)),
            MailRuleSetChangeOutcome.VersionConflict => Superseded(write, result.Version),
            MailRuleSetChangeOutcome.RuleNotFound => Refused($"{NoSuchRule} Nothing changed."),
            _ => Refused(DescribeErrors("The change was refused, and nothing changed:", result.Errors)),
        };

    private static ProblemHttpResult Superseded(MailRuleSetWrite write, long currentVersion) => TypedResults.Problem(
        $"This account's rules are at version {currentVersion}, and the change was written against version {write.ExpectedVersion}, so nothing changed. Read the rules again and decide again.",
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [RouteAuthorization.ErrorCodeExtension] = MailFathomErrorCode.MailRuleSetVersionSuperseded.Value,
        });

    /// <summary>Reports one account's rules as the file an import reads.</summary>
    /// <param name="account">The identifier the deployment generated for the account whose rules are exported.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="ruleSets">Reads the account's stored set.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account the request names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the file, including one with no rules, or <c>400</c>.</returns>
    /// <remarks>Under the read grant the stored set is read under, because the file says nothing the set does not.</remarks>
    internal static async Task<Results<Ok<MailRuleSetDocument>, ProblemHttpResult>> ExportRulesAsync(
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetReader ruleSets,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleSets);

        if (await AdminAccountRequest.ResolveCoveredAsync(account, MailFathomPermission.AdminRead, accounts, authorization, cancellationToken) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        var reading = await ruleSets.ReadAsync(servedAccount, cancellationToken);

        return TypedResults.Ok(MailRuleSetDocument.For(reading.Stored.Declarations));
    }

    /// <summary>Brings a whole set of rules into one account, replacing its rules or merging into them.</summary>
    /// <param name="request">The account, the version read, the mode, and the rules.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="editor">Writes the account's rules.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with what the import did to each rule, <c>409</c> for a stale version, or <c>400</c>.</returns>
    internal static Task<Results<Ok<MailRuleImportResponse>, ProblemHttpResult>> ImportRulesAsync(
        [FromBody] MailRuleImportRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetEditor editor,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return ImportAsync(request, accounts, authorization, MailFathomPermission.AdminConfigurationWrite, editor.ImportAsync, cancellationToken);
    }

    /// <summary>Reports what an import would do to one account's rules, judging it as the import would, and writes nothing.</summary>
    /// <param name="request">The account, the version read, the mode, and the rules.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="editor">Judges the import.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account.</param>
    /// <param name="cancellationToken">Cancels the preview.</param>
    /// <returns><c>200</c> with what the import would do to each rule, <c>409</c> for a stale version, or <c>400</c>.</returns>
    internal static Task<Results<Ok<MailRuleImportResponse>, ProblemHttpResult>> PreviewImportAsync(
        [FromBody] MailRuleImportRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailRuleSetEditor editor,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return ImportAsync(request, accounts, authorization, MailFathomPermission.AdminRead, editor.PreviewImportAsync, cancellationToken);
    }

    private static async Task<Results<Ok<MailRuleImportResponse>, ProblemHttpResult>> ImportAsync(
        MailRuleImportRequest? request,
        IDeploymentMailAccountCatalog accounts,
        AccessAuthorization authorization,
        MailFathomPermission permission,
        Func<MailRuleSetWrite, MailRuleSetImport, CancellationToken, Task<MailRuleSetImportResult>> import,
        CancellationToken cancellationToken)
    {
        if (await AdminAccountRequest.ResolveCoveredAsync(request?.Account, permission, accounts, authorization, cancellationToken) is not { } account)
        {
            return AdminAccountRequest.Refuse(request?.Account);
        }

        if (request!.Version is not { } version || version < 0)
        {
            return Refused(NoVersionStated);
        }

        if (ReadImportMode(request.Mode) is not { } mode)
        {
            return Refused("An import states its Mode: Replace, which makes the account's rules exactly the imported ones and removes every other, or Merge, which keeps every rule the import does not name.");
        }

        if (request.Rules is not { } rules)
        {
            return Refused("An import carries its Rules, a list of rule objects, which may be empty.");
        }

        var read = rules
            .Select((rule, index) => (Draft: MailRuleDeclarationRequest.Read(rule, out var refusal), Refusal: refusal, Label: $"#{index + 1}"))
            .ToArray();

        if (read.Any(rule => rule.Draft is null))
        {
            return Refused(DescribeErrors(
                "The import was refused, and nothing changed:",
                [.. read.Where(rule => rule.Draft is null).Select(rule => $"Rule {rule.Label} of the import: {rule.Refusal}")]));
        }

        var result = await import(
            new MailRuleSetWrite(account, version),
            new MailRuleSetImport(mode, [.. read.Select(rule => rule.Draft!)]),
            cancellationToken);

        return result.Outcome switch
        {
            MailRuleSetChangeOutcome.Committed or MailRuleSetChangeOutcome.Previewed => TypedResults.Ok(MailRuleImportResponse.For(account, result)),
            MailRuleSetChangeOutcome.VersionConflict => Superseded(new MailRuleSetWrite(account, version), result.Version),
            _ => Refused(DescribeErrors(
                "The import was refused, and nothing changed. A rule of the import is named by its place in it, from #1:",
                result.Errors)),
        };
    }

    private static MailRuleSetImportMode? ReadImportMode(string? mode) => mode?.Trim() switch
    {
        { } written when string.Equals(written, nameof(MailRuleSetImportMode.Replace), StringComparison.OrdinalIgnoreCase) => MailRuleSetImportMode.Replace,
        { } written when string.Equals(written, nameof(MailRuleSetImportMode.Merge), StringComparison.OrdinalIgnoreCase) => MailRuleSetImportMode.Merge,
        _ => null,
    };

    /// <summary>States every defect a refusal found, one to a line, under the sentence saying what was refused.</summary>
    /// <remarks>Each defect names the rule by its identifier, or an import's rule by its place in the file. A condition refusal quotes the one identifier or function the condition could not be read at, or the parser's own message, and goes back only to the caller who wrote it; nothing else the rule declares is quoted.</remarks>
    private static string DescribeErrors(string heading, IReadOnlyList<string> errors) =>
        string.Join('\n', errors.Select(error => $"- {error}").Prepend(heading));

    private static ProblemHttpResult Refused(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);

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
