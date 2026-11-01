// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Maps the routes a person reads the footers their accounts send with by, and writes their own on.</summary>
/// <remarks>
/// Reading is <see cref="MailFathomPermission.ProfileRead" /> and writing is
/// <see cref="MailFathomPermission.ProfileWrite" />, because a footer is what a person says about themselves under
/// their mail rather than mail of its own.
/// </remarks>
internal static class ClientMailFooterEndpoints
{
    /// <summary>The route the footer each of the person's accounts sends with is read at, relative to the client prefix.</summary>
    internal const string MailFootersRoute = "/mail-footers";

    /// <summary>The route the person's own footer for one account is read, written, and removed at, relative to the client prefix.</summary>
    internal const string OwnMailFooterRoute = "/accounts/{accountId}/footer";

    /// <summary>Maps the footer routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailFooters(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailFootersRoute, ReadInForceAsync)
            .RequirePermission(MailFathomPermission.ProfileRead);

        api.MapGet(OwnMailFooterRoute, ReadOwnAsync)
            .RequirePermission(MailFathomPermission.ProfileRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body.
        api.MapPut(OwnMailFooterRoute, SaveOwnAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MailFooterRequest.MaxRequestBytes))
            .RequirePermission(MailFathomPermission.ProfileWrite);

        api.MapDelete(OwnMailFooterRoute, RemoveOwnAsync)
            .RequirePermission(MailFathomPermission.ProfileWrite);
    }

    /// <summary>Hands the person, for each of their accounts, the footer a message sent from it now would carry.</summary>
    /// <param name="footers">The person's own footers.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with one entry per account that sends with a footer; an account sending without one has none.</returns>
    internal static async Task<Ok<ClientMailFootersResponse>> ReadInForceAsync(
        [FromServices] OwnMailFooters footers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(footers);

        var readings = await footers.ReadInForceAsync(cancellationToken);

        return TypedResults.Ok(new ClientMailFootersResponse([.. readings.Select(ClientMailFooterInForceResponse.For)]));
    }

    /// <summary>Hands the person their own footer for one of their accounts, as they would edit it.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="footers">The person's own footers.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the footer, which is absent where they wrote none, or <c>404</c> when the account is not one of theirs.</returns>
    internal static async Task<Results<Ok<ClientOwnMailFooterResponse>, NotFound<ProblemDetails>>> ReadOwnAsync(
        string accountId,
        [FromServices] OwnMailFooters footers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(footers);

        return await footers.ReadAsync(MailAccountId.Create(accountId), cancellationToken) is { } own
            ? TypedResults.Ok(new ClientOwnMailFooterResponse(
                own.Footer is { } footer ? MailFooterResponse.For(footer) : null,
                own.IsSetByOrganization))
            : AccountNotAssigned(accountId);
    }

    /// <summary>Replaces the person's own footer for one of their accounts with the one the body states.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="request">The whole footer, its images included.</param>
    /// <param name="footers">The person's own footers.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <c>200</c> with the footer as saved, its markup sanitized; <c>404</c> when the account is not one of theirs;
    /// <c>409</c> when the organization holding the account sets every member's footer on it; or <c>400</c> naming what
    /// was wrong with the footer — an image linked rather than uploaded among it.
    /// </returns>
    internal static async Task<Results<Ok<MailFooterResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> SaveOwnAsync(
        string accountId,
        [FromBody] MailFooterRequest request,
        [FromServices] OwnMailFooters footers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(footers);

        if (request.Stated(out var malformed) is not { } images)
        {
            return TypedResults.Problem(malformed, statusCode: StatusCodes.Status400BadRequest);
        }

        var write = await footers.SaveAsync(MailAccountId.Create(accountId), request.Html!, images, cancellationToken);

        return write.Outcome switch
        {
            MailFooterWriteOutcome.Saved => TypedResults.Ok(MailFooterResponse.For(write.Saved!)),
            MailFooterWriteOutcome.SetByOrganization => TypedResults.Problem(
                "Your organization sets the footer every message from this account carries, so a footer of your own cannot be written for it.",
                statusCode: StatusCodes.Status409Conflict),
            MailFooterWriteOutcome.Refused => TypedResults.Problem(
                MailFooterRefusals.Describe(write.Refusal!),
                statusCode: StatusCodes.Status400BadRequest),
            _ => AccountNotAssigned(accountId),
        };
    }

    /// <summary>Removes the person's own footer for one of their accounts, so its messages go out without one.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="footers">The person's own footers.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>204</c> whether or not there was one, or <c>404</c> when the account is not one of theirs.</returns>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>>> RemoveOwnAsync(
        string accountId,
        [FromServices] OwnMailFooters footers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(footers);

        return await footers.RemoveAsync(MailAccountId.Create(accountId), cancellationToken)
            ? TypedResults.NoContent()
            : AccountNotAssigned(accountId);
    }

    private static NotFound<ProblemDetails> AccountNotAssigned(string accountId) => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = $"Account '{accountId}' is not one of yours.",
    });
}

/// <summary>The footer each of a person's accounts sends with.</summary>
/// <param name="Footers">One entry per account that sends with a footer.</param>
internal sealed record ClientMailFootersResponse(IReadOnlyList<ClientMailFooterInForceResponse> Footers);

/// <summary>The footer one account sends with, as the client draws it.</summary>
/// <param name="Account">The account's identifier.</param>
/// <param name="Source"><c>own</c> for the person's own footer, or <c>organization</c> for the template their organization sets.</param>
/// <param name="Document">The footer as the closed document tree a client draws, its images inlined, or <see langword="null" /> where it reduced to nothing.</param>
internal sealed record ClientMailFooterInForceResponse(string Account, string Source, MailDocument? Document)
{
    /// <summary>Describes one reading on the wire.</summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The response entry.</returns>
    internal static ClientMailFooterInForceResponse For(MailFooterInForceReading reading) => new(
        reading.Account.Value,
        reading.Source is MailFooterSource.Organization ? "organization" : "own",
        reading.Document);
}

/// <summary>A person's own footer for one account, as they would edit it.</summary>
/// <param name="Footer">The footer they wrote, or <see langword="null" /> for none.</param>
/// <param name="SetByOrganization">Whether the organization holding the account sets the footer instead, which leaves theirs unwritable.</param>
internal sealed record ClientOwnMailFooterResponse(MailFooterResponse? Footer, bool SetByOrganization);
