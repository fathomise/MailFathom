// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Categories;
using MailFathom.Application.Classification;
using MailFathom.Application.Folders;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Access;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the one operation that goes over a mailbox already stored: spam first, then the categories of what is not spam.</summary>
/// <remarks>
/// <para>
/// Nothing here switches either function on or widens what it covers: those are configuration, so what an instance will do
/// to a mailbox is reviewable in a diff before it runs. Asking for a run is <c>mailfathom.admin.operate</c>, because it can
/// start a pass over a whole mailbox and, with acting asked for, move a thousand messages, and reading where one got to is
/// <c>mailfathom.admin.read</c>.
/// </para>
/// <para>Nothing either answers with is mail: counts, instants, folder aliases, and the account's own names.</para>
/// </remarks>
internal static class ClassificationRunEndpoints
{
    /// <summary>The route a whole-mailbox run is asked for and read from, relative to the administrative prefix.</summary>
    /// <remarks>
    /// One path and two verbs, because they are one operation performed and then watched: what the reading reports is the
    /// run the write asked for, and an operator who started a pass over their mailbox comes back to the same place to
    /// find out where it got to.
    /// </remarks>
    internal const string RunsRoute = "/classification/runs";

    /// <summary>The greatest request body the run route reads before refusing it.</summary>
    /// <remarks>
    /// The body names one account, a handful of folder aliases, one category, and two switches, so a few kilobytes is the
    /// whole of anything it could mean. Stated because the server's own default is measured in tens of megabytes, which for
    /// this route would let an authenticated client make the process buffer a body four orders of magnitude larger than
    /// the request it is sending.
    /// </remarks>
    internal const int MaxRunRequestBytes = 8 * 1024;

    /// <summary>Maps the run routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClassificationRuns(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the rule run route does: it
        // implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body
        // over the bound is answered 413 before the handler is reached.
        api.MapPost(RunsRoute, StartRunAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRunRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminOperate);

        api.MapGet(RunsRoute, ReadRunAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);
    }

    /// <summary>Asks for every message stored for one account to be gone over on the terms the caller named.</summary>
    /// <param name="request">The account, the scope, the two switches, and the one own category.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="mailSettings">Holds the account settings the scope is read from, which this route prepares for the named account.</param>
    /// <param name="requests">Records the request, or reports the run already in front of the account.</param>
    /// <param name="folders">Reads which of the account's folders are visible to AI, which is what categories cover.</param>
    /// <param name="context">The request being answered, whose services the account's settings are resolved through once they are prepared.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account the request names.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the run, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// It records that the run is wanted and classifies nothing. The pass is a step of the account's synchronization run,
    /// so the request neither performs the work nor keeps it alive — which is what stops an operator's terminal closing
    /// from cancelling a walk of their mailbox, and what makes this answer immediately however large the mailbox is.
    /// <para>
    /// A second request while one is outstanding is answered with the run already under way rather than refused, and the
    /// terms this request carried are not applied to it: a walk that has scored half a mailbox as a dry run cannot become
    /// one that acts halfway through. The answer says which of the two happened and reports the terms the outstanding run
    /// is actually walking under.
    /// </para>
    /// <para>
    /// An administrative caller acts for no user, so what the account covers — the default and the bound — is read only
    /// after the named account's settings are prepared.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClassificationRunStartResponse>, ProblemHttpResult>> StartRunAsync(
        [FromBody] ClassificationRunRequestBody? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] ScopedMailSynchronizationSettings mailSettings,
        [FromServices] ClassificationRunRequests requests,
        [FromServices] IDeploymentMailFolders folders,
        HttpContext context,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(mailSettings);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(context);

        if (await AdminAccountRequest.ResolveCoveredAsync(request?.Account, MailFathomPermission.AdminOperate, accounts, authorization, cancellationToken) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(request?.Account);
        }

        if (!await mailSettings.UseAccountSettingsAsync(servedAccount, cancellationToken))
        {
            return AdminAccountRequest.Unknown(servedAccount.Value);
        }

        var spam = context.RequestServices
            .GetRequiredService<ISpamClassificationSettingsReader>()
            .SettingsFor(servedAccount);
        var categorization = context.RequestServices
            .GetRequiredService<IEmailCategorizationSettingsReader>()
            .SettingsFor(servedAccount);

        if (!spam.IsEnabled && !categorization.IsEnabled)
        {
            return Refuse("This account has neither SpamClassification:Enabled nor Categorization:Enabled switched on, so a run over it would do nothing. Switch one of them on first.");
        }

        var (ownCategory, categoryRefusal) = ResolveOwnCategory(request, categorization);

        if (categoryRefusal is not null)
        {
            return Refuse(categoryRefusal);
        }

        IReadOnlyList<MailFolderAlias> visibleFolders = categorization.IsEnabled
            ? [.. (await folders.ReadAsync(MailFolderSelection.VisibleToAI, [servedAccount], cancellationToken)).Select(static folder => folder.Alias)]
            : [];

        var scope = ResolveScope(
            request?.Folders,
            ownCategory is null && spam.IsEnabled ? spam.ScannedFolderAliases : [],
            visibleFolders);

        if (scope.Refusal is { } scopeRefusal)
        {
            return Refuse(scopeRefusal);
        }

        var terms = ClassificationRunTerms.Create(
            scope.SpamFolderAliases,
            scope.CategoryFolderAliases,
            request?.Apply is true ? SpamActionPosture.Acting : SpamActionPosture.DryRun,
            request?.Rescore is true,
            ownCategory);

        var submitted = await requests.SubmitAsync(servedAccount, terms, cancellationToken);

        return TypedResults.Ok(new ClassificationRunStartResponse(
            submitted.Accepted,
            ClassificationRunResponse.For(submitted.Run)));
    }

    /// <summary>Reports where one account's whole-mailbox classification run has got to, or how the last one ended.</summary>
    /// <param name="account">The identifier the deployment generated for the account whose run is read.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="runs">Reads the one run an account may have outstanding, for a caller the read's own grant admits.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the account the request names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the run, <c>200</c> with none where the account has never been asked for one, or <c>400</c>.</returns>
    /// <remarks>
    /// An account that has never been asked for a run is an outcome rather than a refusal, so the answer carries no run
    /// instead of a <c>404</c>: the caller asked a question this deployment can answer, and the answer is that nothing
    /// has been asked for.
    /// </remarks>
    internal static async Task<Results<Ok<ClassificationRunStateResponse>, ProblemHttpResult>> ReadRunAsync(
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] ClassificationRunReader runs,
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

        return TypedResults.Ok(new ClassificationRunStateResponse(
            servedAccount.Value,
            run is null ? null : ClassificationRunResponse.For(run)));
    }

    private static ProblemHttpResult Refuse(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Finds the one own category a request names, spelt as the account spells it, or says why it cannot be applied.</summary>
    /// <remarks>A refusal never repeats the name, which is a subject somebody chose and can be personal data, and a refusal is written to a log.</remarks>
    private static (string? OwnCategory, string? Refusal) ResolveOwnCategory(
        ClassificationRunRequestBody? request,
        EmailCategorizationSettings categorization)
    {
        if (request?.Category is not { } named)
        {
            return (null, null);
        }

        if (request.Apply is true || request.Rescore is true)
        {
            return (null, "Applying one category always asks and takes no action on the mailbox, so it cannot be combined with rescoring or applying.");
        }

        if (!categorization.IsEnabled)
        {
            return (null, "This account does not sort its mail into categories, so there is no category to apply. Switch Categorization:Enabled on first.");
        }

        return categorization.OwnCategoryNamed(named) is { } category
            ? (category.Name, null)
            : (null, "This account names no own category by that name.");
    }

    /// <summary>Resolves the folders each half walks, defaulting to and bounded by what each function covers.</summary>
    /// <remarks>
    /// A folder neither covers is refused rather than walked, because a run over it would read a whole folder and record
    /// nothing. A folder only one covers gets only that half, which keeps a request naming a folder the categories half
    /// cannot reach from being refused for the half it can.
    /// </remarks>
    private static (IReadOnlyList<MailFolderAlias> SpamFolderAliases, IReadOnlyList<MailFolderAlias> CategoryFolderAliases, string? Refusal) ResolveScope(
        IReadOnlyList<string>? requested,
        IReadOnlyList<MailFolderAlias> spamScope,
        IReadOnlyList<MailFolderAlias> categoryScope)
    {
        if (requested is null)
        {
            return spamScope.Count > 0 || categoryScope.Count > 0
                ? (spamScope, categoryScope, null)
                : ([], [], "This account covers no folder, so a run over one would read nothing. Name the folders in its classification settings, or map one visible to AI, first.");
        }

        var named = requested.Where(static alias => !string.IsNullOrWhiteSpace(alias)).ToArray();

        if (named.Length == 0)
        {
            return ([], [], "The request named no folder to classify. Leave the folders out to walk everything the account covers.");
        }

        var unusable = named.FirstOrDefault(static alias => !IsUsableAlias(alias));

        if (unusable is not null)
        {
            return ([], [], $"'{unusable}' is not a usable folder alias.");
        }

        MailFolderAlias[] aliases = [.. named.Select(MailFolderAlias.Create)];
        var outside = aliases.FirstOrDefault(alias => !spamScope.Contains(alias) && !categoryScope.Contains(alias));

        return outside == default
            ? ([.. aliases.Where(alias => spamScope.Contains(alias))], [.. aliases.Where(alias => categoryScope.Contains(alias))], null)
            : ([], [], $"Neither function this run could cover reaches folder '{outside.Value}', so a run over it would record nothing. Add it to the account's classification scope or map it visible to AI first.");
    }

    /// <summary>Reports whether text a caller sent is a value this system could have issued as an alias.</summary>
    /// <remarks>
    /// Asked before the value object is built rather than by catching what it throws, because a caller's text is
    /// untrusted input at the boundary and a refusal is what a <c>400</c> is made of.
    /// </remarks>
    private static bool IsUsableAlias(string alias) => !alias.Any(char.IsControl);
}
