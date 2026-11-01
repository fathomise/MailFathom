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

/// <summary>Serves the signed-in member their own presence: the clients they keep open, the state they choose, their status message, and where they work from.</summary>
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

    /// <summary>The route the caller's status message is written at, relative to the client prefix.</summary>
    internal const string StatusMessageRoute = "/presence/status-message";

    /// <summary>The route the caller's work mode is written at, relative to the client prefix.</summary>
    internal const string WorkModeRoute = "/presence/work-mode";

    /// <summary>The greatest request body a write here reads before refusing it.</summary>
    /// <remarks>Two short words; anything near this size is not a presence write.</remarks>
    internal const int MaxWriteRequestBytes = 256;

    /// <summary>The greatest request body a status message write reads before refusing it.</summary>
    /// <remarks>The longest message with every character escaped in six bytes is under two kilobytes.</remarks>
    internal const int MaxStatusMessageRequestBytes = 4_096;

    /// <summary>What a refused status message is answered with.</summary>
    /// <remarks>Fixed rather than taken from the exception, so nothing but the domain's own rule can reach the caller in it.</remarks>
    private static readonly string StatusMessageRefusal =
        $"State the message as one line of printable text of at most {StatusMessage.MostCharacters} characters, or as nothing to clear it.";

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

        api.MapPut(StatusMessageRoute, WriteStatusMessageAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxStatusMessageRequestBytes))
            .RequirePermission(MailFathomPermission.ProfileWrite);

        api.MapPut(WorkModeRoute, ChooseWorkModeAsync)
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

    /// <summary>Records the status message the caller wrote.</summary>
    /// <param name="request">The message and when it clears.</param>
    /// <param name="presence">The caller's own presence.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the caller's presence after the write, <c>400</c> for a message the domain refuses — too long, or holding a character that renders as something other than text — or for a while this surface does not know, or <c>404</c> where the caller belongs to no organization.</returns>
    /// <remarks>A message of nothing but white space clears it.</remarks>
    internal static async Task<Results<Ok<OwnPresenceResponse>, NotFound, ProblemHttpResult>> WriteStatusMessageAsync(
        [FromBody] StatusMessageRequest request,
        [FromServices] OwnPresence presence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(presence);

        if (request.Text is not { } text)
        {
            return Refuse(StatusMessageRefusal);
        }

        PresenceDuration? clears = null;

        if (request.Clears is not null)
        {
            if (DurationNamed(request.Clears) is not { } named)
            {
                return Refuse("State when the message clears as 30m, 1h, 2h, today, or week, or leave it out for never.");
            }

            clears = named;
        }

        OwnPresenceReading? reading;

        try
        {
            reading = await presence.WriteStatusMessageAsync(text, clears, cancellationToken);
        }
        catch (ArgumentException)
        {
            return Refuse(StatusMessageRefusal);
        }

        return reading is not null ? TypedResults.Ok(OwnPresenceResponse.For(reading)) : TypedResults.NotFound();
    }

    /// <summary>Records where the caller works from.</summary>
    /// <param name="request">Office or remote, and whether for today alone.</param>
    /// <param name="presence">The caller's own presence.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the caller's presence after the write, <c>400</c> for a mode this surface does not know or no word on today, or <c>404</c> where the caller belongs to no organization.</returns>
    internal static async Task<Results<Ok<OwnPresenceResponse>, NotFound, ProblemHttpResult>> ChooseWorkModeAsync(
        [FromBody] WorkModeRequest request,
        [FromServices] OwnPresence presence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(presence);

        // By name alone, for the reason a presence state is.
        if (request.Mode is not { } stated
            || !Enum.GetNames<WorkMode>().Contains(stated, StringComparer.Ordinal)
            || request.Today is not { } today)
        {
            return Refuse("State the work mode as Office or Remote, and today as true or false.");
        }

        return await presence.ChooseWorkModeAsync(Enum.Parse<WorkMode>(stated), today, cancellationToken) is { } reading
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

/// <summary>The status message a member writes.</summary>
/// <param name="Text">The message, of at most 280 characters on one line; nothing but white space clears it.</param>
/// <param name="Clears"><c>30m</c>, <c>1h</c>, <c>2h</c>, <c>today</c>, or <c>week</c>, or nothing for never.</param>
/// <remarks>Bound strictly, so a field this surface never published fails the bind rather than being ignored.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record StatusMessageRequest(string? Text = null, string? Clears = null);

/// <summary>Where a member says they work from.</summary>
/// <param name="Mode"><c>Office</c> or <c>Remote</c>.</param>
/// <param name="Today"><see langword="true" /> for today alone, <see langword="false" /> until changed.</param>
/// <remarks>Bound strictly, so a field this surface never published fails the bind rather than being ignored.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record WorkModeRequest(string? Mode = null, bool? Today = null);

/// <summary>A member's presence as they see it themselves.</summary>
/// <param name="State">Their state, <c>Hidden</c> included.</param>
/// <param name="ChosenState">The state they chose while the choice holds, or nothing where their presence is automatic.</param>
/// <param name="ChoiceDuration">The while the choice was made for, named as a choice states it — <c>30m</c>, <c>1h</c>, <c>2h</c>, <c>today</c>, or <c>week</c> — or nothing where it lasts until changed or there is none.</param>
/// <param name="ChoiceEndsAt">When the choice runs out, or nothing where it lasts until changed or there is none.</param>
/// <param name="StatusMessage">Their status message while it has not cleared, or nothing.</param>
/// <param name="WorkMode">Where they said they work from while it holds, or nothing.</param>
internal sealed record OwnPresenceResponse(
    string State,
    string? ChosenState,
    string? ChoiceDuration,
    DateTimeOffset? ChoiceEndsAt,
    StatusMessageResponse? StatusMessage,
    WorkModeResponse? WorkMode)
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
            reading.Choice?.EndsAt,
            StatusMessageResponse.For(reading.Message),
            WorkModeResponse.For(reading.WorkMode));
    }
}

/// <summary>What a member tells the other members of their organization they are doing.</summary>
/// <param name="Text">The message.</param>
/// <param name="Clears">The while it was written for, named as a write states it, or nothing where it never clears.</param>
/// <param name="ClearsAt">When it clears, or nothing where it never does.</param>
/// <remarks>Personal data about the member, travelling to them and to the members of their organization alone.</remarks>
internal sealed record StatusMessageResponse(string Text, string? Clears, DateTimeOffset? ClearsAt)
{
    /// <summary>Describes a status message.</summary>
    /// <param name="message">The message in effect, or <see langword="null" />.</param>
    /// <returns>The response body, or <see langword="null" /> where there is no message.</returns>
    internal static StatusMessageResponse? For(StatusMessage? message) =>
        message is null
            ? null
            : new StatusMessageResponse(
                message.Text,
                message.Clears is { } clears ? ClientPresenceEndpoints.NameOf(clears) : null,
                message.ClearsAt);
}

/// <summary>Where a member said they work from.</summary>
/// <param name="Mode"><c>Office</c> or <c>Remote</c>.</param>
/// <param name="EndsAt">The member's next midnight where they said it for today alone, or nothing where it lasts until changed.</param>
internal sealed record WorkModeResponse(string Mode, DateTimeOffset? EndsAt)
{
    /// <summary>Describes a work mode.</summary>
    /// <param name="workMode">The work mode in effect, or <see langword="null" />.</param>
    /// <returns>The response body, or <see langword="null" /> where none is stated.</returns>
    internal static WorkModeResponse? For(WorkModeChoice? workMode) =>
        workMode is null ? null : new WorkModeResponse(workMode.Mode.ToString(), workMode.EndsAt);
}
