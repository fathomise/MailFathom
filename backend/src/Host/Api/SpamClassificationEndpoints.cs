// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Spam.History;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves what spam classification concluded about a mailbox that is already stored.</summary>
/// <remarks>
/// <para>
/// Reading back what was concluded and what it asked for; asking for a whole mailbox to be gone over is
/// <see cref="ClassificationRunEndpoints" />. Nothing here switches classification on, widens the folders it covers, or names a junk folder: those are configuration, which
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md">ADR 0002</see>
/// keeps read-only so that what an instance will do to a mailbox is reviewable in a diff before it runs.
/// </para>
/// <para>
/// It is here rather than on the MCP surface because none of it is anything a model reasons over, and the classifications
/// themselves are <c>mailfathom.admin.audit.read</c>: a verdict about a message is derived from that message rather than a
/// report of the deployment's own state.
/// </para>
/// <para>
/// Nothing it answers with is mail. Counts, verdicts, scores, signal names, folder aliases, mutation names,
/// instants, and identifiers are the whole of it — a signal's observed value never leaves the database, because it is
/// text a mail server wrote and can carry a sending domain.
/// </para>
/// </remarks>
internal static class SpamClassificationEndpoints
{
    /// <summary>The route the recorded classifications are read from, relative to the administrative prefix.</summary>
    internal const string ClassificationsRoute = "/spam/classifications";

    /// <summary>Maps the classification route into the administrative group, so it inherits its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapSpamClassification(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ClassificationsRoute, ReadClassificationsAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminAuditRead);
    }

    /// <summary>Serves one page of what classification concluded about an account's mail.</summary>
    /// <param name="account">The identifier the deployment generated for the account whose classifications are read.</param>
    /// <param name="email">The local identity of the occurrence to narrow to, or <see langword="null" /> for every one.</param>
    /// <param name="verdict">The verdict to narrow to, or <see langword="null" /> for every verdict.</param>
    /// <param name="from">The earliest evaluation instant served, inclusive, or <see langword="null" /> for none.</param>
    /// <param name="before">The evaluation instant to stop before, exclusive, or <see langword="null" /> for none.</param>
    /// <param name="pageSize">How many classifications the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="classifications">Reads the page, for a caller the record's own grant admits.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the named account.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c>, including an account this deployment does not configure, which mirrors the rule
    /// history and the two audit trails beside it: an unknown account is a mistake in the request the caller wrote rather
    /// than a missing resource, and <c>404</c> is already what a client reads as "this port serves no administrative
    /// endpoint".
    /// </remarks>
    internal static async Task<Results<Ok<SpamClassificationPageResponse>, ProblemHttpResult>> ReadClassificationsAsync(
        [FromQuery] string? account,
        [FromQuery] Guid? email,
        [FromQuery] string? verdict,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] SpamClassificationHistory classifications,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(classifications);

        if (await AdminAccountRequest.ResolveCoveredAsync(
                account,
                MailFathomPermission.AdminAuditRead,
                accounts,
                authorization,
                cancellationToken) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        SpamClassificationHistoryCursor? decodedCursor = null;

        if (cursor is not null && !SpamClassificationHistoryCursor.TryDecode(cursor, out decodedCursor))
        {
            return TypedResults.Problem(
                "The continuation cursor is not one this deployment issued.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryReadVerdict(verdict, out var namedVerdict))
        {
            return TypedResults.Problem(
                $"The verdict filter names no verdict this deployment reaches. It is one of {DeclaredVerdicts()}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var queryResult = SpamClassificationHistoryQuery.Create(
            servedAccount,
            email is { } emailId ? StoredEmailId.Create(emailId) : null,
            namedVerdict,
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

        var page = await classifications.ReadPageAsync(query, cancellationToken);

        return TypedResults.Ok(SpamClassificationPageResponse.For(page));
    }

    /// <summary>Reads the verdict a filter named, keeping "no filter" apart from "a name nothing answers to".</summary>
    private static bool TryReadVerdict(string? verdict, out SpamVerdict? namedVerdict)
    {
        namedVerdict = null;

        if (verdict is null)
        {
            return true;
        }

        if (!Enum.TryParse<SpamVerdict>(verdict, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return false;
        }

        namedVerdict = parsed;

        return true;
    }

    private static string DeclaredVerdicts() => string.Join(", ", Enum.GetNames<SpamVerdict>());

    /// <summary>States what a caller has to change, without restating the filters they already sent.</summary>
    private static string DescribeRefusal(SpamClassificationHistoryQueryOutcome outcome) => outcome switch
    {
        SpamClassificationHistoryQueryOutcome.PageSizeOutOfRange =>
            $"A page of classifications holds between 1 and {SpamClassificationHistoryQuery.MaximumPageSize} records.",
        SpamClassificationHistoryQueryOutcome.TimeRangeEmpty =>
            "The time range ends at or before it begins, so it names no classifications.",
        SpamClassificationHistoryQueryOutcome.VerdictUnknown =>
            $"The verdict filter names no verdict this deployment reaches. It is one of {DeclaredVerdicts()}.",
        _ => "The continuation cursor was issued for a different set of classification filters.",
    };
}
