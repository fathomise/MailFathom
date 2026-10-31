// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.AutomaticReplies;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

internal static class ClientAutomaticReplyEndpoint
{
    /// <summary>The route the acting person's automatic reply is read at and written back to, relative to the client prefix.</summary>
    internal const string AutomaticReplyRoute = "/automatic-reply";

    /// <summary>The greatest request body the write route reads before refusing it.</summary>
    /// <remarks>
    /// Two texts of <see cref="AutomaticReply.MaximumTextLength" /> characters at the widest JSON escaping of each, the
    /// longest list of account identifiers, and the period. A body past it is answered <c>413</c> before the handler is
    /// reached, as every other write on this surface is.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 128 * 1024;

    /// <summary>Maps the automatic reply routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientAutomaticReply(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(AutomaticReplyRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.ProfileRead);

        // The attribute is reached for its metadata rather than as an MVC filter, for the reason the record routes
        // state: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body.
        api.MapPost(AutomaticReplyRoute, SaveAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.ProfileWrite);
    }

    /// <summary>Hands the acting person the automatic reply they set.</summary>
    /// <param name="replies">The acting person's own automatic reply.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the reply, or <c>204</c> when they never set one.</returns>
    internal static async Task<Results<Ok<ClientAutomaticReplyResponse>, NoContent>> ReadAsync(
        [FromServices] OwnAutomaticReply replies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replies);

        return await replies.ReadAsync(cancellationToken) is { } stored
            ? TypedResults.Ok(ClientAutomaticReplyResponse.For(stored.Reply))
            : TypedResults.NoContent();
    }

    /// <summary>Replaces the acting person's automatic reply with the one the body states.</summary>
    /// <param name="replies">The acting person's own automatic reply.</param>
    /// <param name="request">The whole reply.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <c>200</c> with the reply as saved, <c>404</c> when this deployment holds no record for the caller, or <c>400</c>
    /// when the body is not a reply — a missing text for senders outside the organization, a text too long, no account,
    /// an account named twice, a period that ends before it starts — or names an account that is not the caller's or is
    /// a shared mailbox, naming that account, or carries a text for colleagues while the caller is in no organization.
    /// </returns>
    /// <remarks>
    /// Every save starts the reply over: each sender is answered once per save, so a sender answered under the reply
    /// this one replaces is answered again under this one. Switching the reply off is a save with <c>on</c> false.
    /// </remarks>
    internal static async Task<Results<Ok<ClientAutomaticReplyResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> SaveAsync(
        [FromServices] OwnAutomaticReply replies,
        [FromBody] ClientAutomaticReplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replies);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Stated(out var refusal) is not { } reply)
        {
            return Refusal(refusal!);
        }

        var write = await replies.SaveAsync(reply, cancellationToken);

        return write.Outcome switch
        {
            AutomaticReplyWriteOutcome.Saved => TypedResults.Ok(ClientAutomaticReplyResponse.For(write.Saved!.Reply)),
            AutomaticReplyWriteOutcome.AccountNotAssigned => Refusal(
                $"Account '{write.Account}' is not one of yours, so your automatic reply cannot answer on it."),
            AutomaticReplyWriteOutcome.AccountShared => Refusal(
                $"Account '{write.Account}' is a shared mailbox, which never answers automatically: nobody is away on its behalf."),
            AutomaticReplyWriteOutcome.NoOrganization => Refusal(
                "You are a member of no organization, so your automatic reply carries the text for senders outside it alone."),
            _ => TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "This deployment holds no record for you.",
            }),
        };
    }

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>The automatic reply a person states on the write route.</summary>
/// <param name="InsideText">The text for senders inside their organization, or <see langword="null" /> for none.</param>
/// <param name="OutsideText">The text for every other sender.</param>
/// <param name="Accounts">The identifiers of the accounts it answers on.</param>
/// <param name="StartsAt">When answering starts.</param>
/// <param name="EndsAt">When answering stops.</param>
/// <param name="On">Whether it is switched on, which it is unless the body says otherwise.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientAutomaticReplyRequest(
    string? InsideText = null,
    string? OutsideText = null,
    IReadOnlyList<string>? Accounts = null,
    DateTimeOffset? StartsAt = null,
    DateTimeOffset? EndsAt = null,
    bool? On = null)
{
    /// <summary>Reads the body as a reply, or says why it is not one.</summary>
    /// <param name="refusal">Why it is not one, when the answer is <see langword="null" />.</param>
    /// <returns>The reply, or <see langword="null" />.</returns>
    internal AutomaticReply? Stated(out string? refusal)
    {
        if (this.StartsAt is not { } startsAt || this.EndsAt is not { } endsAt)
        {
            refusal = "An automatic reply states when its period starts and when it ends.";

            return null;
        }

        if ((this.Accounts ?? []).Any(string.IsNullOrWhiteSpace))
        {
            refusal = "An automatic reply names each account by its identifier.";

            return null;
        }

        AutomaticReply.TryCreate(
            this.InsideText,
            this.OutsideText,
            [.. (this.Accounts ?? []).Select(MailAccountId.Create)],
            startsAt,
            endsAt,
            this.On ?? true,
            out var reply,
            out refusal);

        return reply;
    }
}

/// <summary>An automatic reply as the client endpoint states it.</summary>
/// <param name="InsideText">The text for senders inside the person's organization, or <see langword="null" /> where they get the other one.</param>
/// <param name="OutsideText">The text for every other sender.</param>
/// <param name="Accounts">The identifiers of the accounts it answers on.</param>
/// <param name="StartsAt">When answering starts.</param>
/// <param name="EndsAt">When answering stops.</param>
/// <param name="On">Whether it is switched on.</param>
internal sealed record ClientAutomaticReplyResponse(
    string? InsideText,
    string OutsideText,
    IReadOnlyList<string> Accounts,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool On)
{
    /// <summary>Describes one reply on the wire.</summary>
    /// <param name="reply">The reply.</param>
    /// <returns>The response body.</returns>
    internal static ClientAutomaticReplyResponse For(AutomaticReply reply) => new(
        reply.InsideText,
        reply.OutsideText,
        [.. reply.Accounts.Select(static account => account.Value)],
        reply.StartsAt,
        reply.EndsAt,
        reply.IsOn);
}
