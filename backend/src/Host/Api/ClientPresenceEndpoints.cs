// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Presence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in member their own presence: the clients they keep open, and the state they choose.</summary>
/// <remarks>
/// <para>
/// A running client renews its own row every minute while it is open and removes it when it closes, so what keeps a
/// member present is a client saying so over a route that is authorized and bounded, rather than a method on the
/// signal hub. A colleague's presence is read with the colleague, over the colleague routes.
/// </para>
/// <para>
/// A caller in no organization is answered <c>404</c> on every route here, because nobody could see their presence and
/// nothing is recorded about them; a client reading that stops renewing.
/// </para>
/// <para>
/// Nothing here is audited or logged. A renewal changes no mail, and an entry per renewal would be the history of
/// somebody's working day that
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0045-a-members-presence-from-the-clients-it-keeps-open-and-the-state-it-chose.md">ADR 0045</see>
/// refuses to keep.
/// </para>
/// </remarks>
internal static class ClientPresenceEndpoints
{
    /// <summary>The route the caller's own presence is read at, relative to the client prefix.</summary>
    internal const string PresenceRoute = "/presence";

    /// <summary>The route one of the caller's open clients is renewed and closed at, relative to the client prefix.</summary>
    internal const string ClientRoute = "/presence/clients/{client:guid}";

    /// <summary>The route the caller's chosen presence is written at, relative to the client prefix.</summary>
    internal const string ChoiceRoute = "/presence/choice";

    /// <summary>The greatest request body a write here reads before refusing it.</summary>
    /// <remarks>Two short words; anything near this size is not a presence write.</remarks>
    internal const int MaxWriteRequestBytes = 256;

    /// <summary>The name each duration is written by on the wire, in both directions.</summary>
    private static readonly (string Name, PresenceDuration Duration)[] DurationNames =
    [
        ("30m", PresenceDuration.ThirtyMinutes),
        ("1h", PresenceDuration.OneHour),
        ("2h", PresenceDuration.TwoHours),
        ("today", PresenceDuration.Today),
        ("week", PresenceDuration.ThisWeek),
    ];

    /// <summary>Maps the presence routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientPresence(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(PresenceRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.ProfileRead);

        // The attribute is reached for its metadata rather than as an MVC filter, for the reason the record routes
        // state: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body.
        api.MapPut(ClientRoute, RenewAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.ProfileWrite);

        api.MapDelete(ClientRoute, CloseAsync)
            .RequirePermission(MailFathomPermission.ProfileWrite);

        api.MapPut(ChoiceRoute, ChooseAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.ProfileWrite);
    }

    /// <summary>Hands the caller their own presence as they see it.</summary>
    /// <param name="presence">The caller's own presence.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the state and the choice in effect, or <c>404</c> where the caller belongs to no organization.</returns>
    internal static async Task<Results<Ok<OwnPresenceResponse>, NotFound>> ReadAsync(
        [FromServices] OwnPresence presence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presence);

        return await presence.ReadAsync(cancellationToken) is { } reading
            ? TypedResults.Ok(OwnPresenceResponse.For(reading))
            : TypedResults.NotFound();
    }

    /// <summary>Renews one of the caller's open clients.</summary>
    /// <param name="client">The identifier the client minted for itself when it started.</param>
    /// <param name="request">Whether a person is using it.</param>
    /// <param name="presence">The caller's own presence.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once renewed, <c>400</c> for an activity this surface does not know, <c>404</c> where the caller belongs to no organization, or <c>409</c> where the caller already keeps the most clients open.</returns>
    internal static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RenewAsync(
        [FromRoute] Guid client,
        [FromBody] PresenceClientRequest request,
        [FromServices] OwnPresence presence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(presence);

        if (client == Guid.Empty || ActivityNamed(request.Activity) is not { } activity)
        {
            return Refuse("State the activity as active or idle, for a client identifier that is not empty.");
        }

        return await presence.RenewAsync(client, activity, cancellationToken) switch
        {
            PresenceWriteOutcome.Recorded => TypedResults.NoContent(),
            PresenceWriteOutcome.NotInOrganization => TypedResults.NotFound(),
            _ => TypedResults.Problem(
                $"At most {MemberPresence.MostOpenClients} clients are kept open at once; close another one first.",
                statusCode: StatusCodes.Status409Conflict),
        };
    }

    /// <summary>Closes one of the caller's clients.</summary>
    /// <param name="client">The identifier the client minted for itself when it started.</param>
    /// <param name="presence">The caller's own presence.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the client is closed, closed already included, or <c>404</c> where the caller belongs to no organization.</returns>
    /// <remarks>A closing client sends this as a <c>keepalive</c> request on its way out and never reads the answer.</remarks>
    internal static async Task<Results<NoContent, NotFound>> CloseAsync(
        [FromRoute] Guid client,
        [FromServices] OwnPresence presence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presence);

        return await presence.CloseAsync(client, cancellationToken) == PresenceWriteOutcome.Recorded
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    /// <summary>Records the presence the caller chose.</summary>
    /// <param name="request">The state and, where it has one, how long it lasts.</param>
    /// <param name="presence">The caller's own presence.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the caller's presence after the choice, <c>400</c> for a state or duration this surface does not know, or <c>404</c> where the caller belongs to no organization.</returns>
    internal static async Task<Results<Ok<OwnPresenceResponse>, NotFound, ProblemHttpResult>> ChooseAsync(
        [FromBody] PresenceChoiceRequest request,
        [FromServices] OwnPresence presence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(presence);

        // By name alone: Enum.TryParse would also take a number or a comma-joined pair of names.
        if (request.State is not { } stated || !Enum.GetNames<PresenceState>().Contains(stated, StringComparer.Ordinal))
        {
            return Refuse("State the presence as Active, Away, Busy, DoNotDisturb, Hidden, or Offline.");
        }

        var state = Enum.Parse<PresenceState>(stated);

        PresenceDuration? duration = null;

        if (request.Duration is not null)
        {
            if (DurationNamed(request.Duration) is not { } named)
            {
                return Refuse("State the duration as 30m, 1h, 2h, today, or week, or leave it out for until changed.");
            }

            duration = named;
        }

        return await presence.ChooseAsync(state, duration, cancellationToken) is { } reading
            ? TypedResults.Ok(OwnPresenceResponse.For(reading))
            : TypedResults.NotFound();
    }

    private static ClientActivity? ActivityNamed(string? activity) => activity switch
    {
        "active" => ClientActivity.Active,
        "idle" => ClientActivity.Idle,
        _ => null,
    };

    /// <summary>Names a duration the way a choice states it.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>Its wire name.</returns>
    internal static string NameOf(PresenceDuration duration) =>
        DurationNames.Single(named => named.Duration == duration).Name;

    private static PresenceDuration? DurationNamed(string duration) =>
        DurationNames.Where(named => named.Name == duration).Select(named => (PresenceDuration?)named.Duration).SingleOrDefault();

    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>What one open client says about itself when it renews.</summary>
/// <param name="Activity"><c>active</c> while a person has used it in the last ten minutes, otherwise <c>idle</c>.</param>
/// <remarks>Bound strictly, so a field this surface never published fails the bind rather than being ignored.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PresenceClientRequest(string? Activity = null);

/// <summary>The presence a member chooses.</summary>
/// <param name="State">One of the six states, by name.</param>
/// <param name="Duration"><c>30m</c>, <c>1h</c>, <c>2h</c>, <c>today</c>, or <c>week</c>, or nothing for until changed.</param>
/// <remarks>Bound strictly, so a field this surface never published fails the bind rather than being ignored.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PresenceChoiceRequest(string? State = null, string? Duration = null);

/// <summary>A member's presence as they see it themselves.</summary>
/// <param name="State">Their state, <c>Hidden</c> included.</param>
/// <param name="ChosenState">The state they chose while the choice holds, or nothing where their presence is automatic.</param>
/// <param name="ChoiceDuration">The while the choice was made for, named as a choice states it — <c>30m</c>, <c>1h</c>, <c>2h</c>, <c>today</c>, or <c>week</c> — or nothing where it lasts until changed or there is none.</param>
/// <param name="ChoiceEndsAt">When the choice runs out, or nothing where it lasts until changed or there is none.</param>
internal sealed record OwnPresenceResponse(string State, string? ChosenState, string? ChoiceDuration, DateTimeOffset? ChoiceEndsAt)
{
    /// <summary>Describes a member's own presence.</summary>
    /// <param name="reading">Their presence as they see it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reading" /> is <see langword="null" />.</exception>
    internal static OwnPresenceResponse For(OwnPresenceReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new OwnPresenceResponse(
            reading.State.ToString(),
            reading.Choice?.State.ToString(),
            reading.Choice?.Duration is { } duration ? ClientPresenceEndpoints.NameOf(duration) : null,
            reading.Choice?.EndsAt);
    }
}
