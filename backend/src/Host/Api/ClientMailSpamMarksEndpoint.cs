// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Spam.Marking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Takes the person's own verdict on a batch of messages: spam, of a kind they may name, or not spam.</summary>
/// <remarks>
/// <para>
/// The person's verdict is final. It outranks every stage of classification, no later stage or reclassification
/// replaces it, and <i>not spam</i> is the one way a spam verdict is lifted. The keywords RFC 9051 defines follow it into
/// the mailbox, so every other mail client reads the correction, and whether the act also moves the message is the
/// account's own switch for filing junk — the client asks before an act that moves, so a call here is taken as
/// confirmed.
/// </para>
/// <para>
/// One request carries one direction and, for spam, one subcategory for the whole batch, because that is the choice the
/// person made: a mixed selection is refused by the client before it gets here, so nothing on this side guesses. Each
/// message is answered on its own and committed on its own, for the reason every batch on this surface is.
/// </para>
/// <para>
/// No route names a user, and nothing here carries mail content: a message identity the caller already holds, a
/// verdict, a subcategory, and the records that carry the change. None of them reaches a log line from here.
/// </para>
/// </remarks>
internal static class ClientMailSpamMarksEndpoint
{
    /// <summary>The route a batch of spam marks is submitted at, relative to the client prefix.</summary>
    internal const string SpamMarksRoute = $"{ClientMailMutationsEndpoint.MutationsRoute}/spam";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, policy, and limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailSpamMarks(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        // The flag grant, because a verdict and its keywords are marks on mail; a mark that also moves a message asks the
        // use case for the moving grant on top of it, which only it can tell from the account's switch.
        api.MapPost(SpamMarksRoute, SubmitSpamMarksAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ClientMailMutationsEndpoint.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailFlagsWrite);
    }

    /// <summary>Records the person's verdict on each message one batch names.</summary>
    /// <param name="request">The verdict, the subcategory for spam, and the messages it is about.</param>
    /// <param name="recorder">Records one message's mark.</param>
    /// <param name="cancellationToken">Cancels the resolutions and the writes when the client disconnects.</param>
    /// <returns><c>200</c> with one result per message, <c>400</c> when the batch itself is not one this boundary accepts, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.flags.write</c>, or <c>mailfathom.mail.move</c> where the account's switch makes a mark move mail.</returns>
    internal static async Task<Results<Ok<ClientMailSpamMarksResponse>, ProblemHttpResult>> SubmitSpamMarksAsync(
        [FromBody] ClientMailSpamMarksRequest? request,
        [FromServices] SpamMarkRecorder recorder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        if (request?.StoredEmailIds is not { Count: > 0 } storedEmailIds)
        {
            return Refusal("A batch of spam marks names at least one message.");
        }

        if (storedEmailIds.Count > ClientMailMutationsEndpoint.MaximumChangesPerRequest)
        {
            return Refusal($"One request carries at most {ClientMailMutationsEndpoint.MaximumChangesPerRequest} changes.");
        }

        if (MarkOf(request) is not { } mark)
        {
            return Refusal(
                $"A spam mark names the verdict '{nameof(SpamVerdict.Spam)}' or '{nameof(SpamVerdict.NotSpam)}', and a "
                + "spam subcategory only beside the first, spelled as the spam object every message carries spells it.");
        }

        if (ClientMailMutationsEndpoint.RequesterOf(request.RequestId) is not { } requester)
        {
            return Refusal(
                "A stated request identifier is at most "
                + $"{MailboxMutationRequester.MaximumIdentityLength} characters and carries no control character.");
        }

        // Every grant the batch needs is asked for before any message is written, so a refusal is the request's answer
        // rather than one arriving after part of the batch committed.
        await recorder.RequireGrantsAsync(
            [.. storedEmailIds.Where(static id => id != Guid.Empty).Select(StoredEmailId.Create)],
            cancellationToken);

        var results = new List<ClientMailSpamMarkResultResponse>(storedEmailIds.Count);

        foreach (var storedEmailId in storedEmailIds)
        {
            results.Add(storedEmailId == Guid.Empty
                ? ClientMailSpamMarkResultResponse.MessageNotFound(storedEmailId)
                : ClientMailSpamMarkResultResponse.For(
                    await recorder.RecordAsync(StoredEmailId.Create(storedEmailId), mark, requester, cancellationToken)));
        }

        return TypedResults.Ok(new ClientMailSpamMarksResponse(results));
    }

    /// <summary>Reads the mark a request states, or nothing where it states none this surface publishes.</summary>
    /// <remarks>
    /// Matched against the member names exactly, never parsed as a number: an enumeration's ordinal is a position, and
    /// a client binding to one would change meaning the day the set is reordered.
    /// </remarks>
    private static SpamMark? MarkOf(ClientMailSpamMarksRequest request)
    {
        SpamSubcategory? subcategory = null;

        if (request.SpamSubcategory is { } named)
        {
            if (!Enum.GetNames<SpamSubcategory>().Contains(named, StringComparer.Ordinal))
            {
                return null;
            }

            subcategory = Enum.Parse<SpamSubcategory>(named);
        }

        return request.Verdict switch
        {
            nameof(SpamVerdict.Spam) => SpamMark.Spam(subcategory),
            nameof(SpamVerdict.NotSpam) when subcategory is null => SpamMark.NotSpam,
            _ => null,
        };
    }

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>One batch of the person's verdicts, one direction for every message it names.</summary>
/// <param name="RequestId">The caller's own identity for this request, or <see langword="null" /> to have one generated.</param>
/// <param name="Verdict"><c>Spam</c> or <c>NotSpam</c>, the member names the spam object every message carries uses.</param>
/// <param name="SpamSubcategory">The kind of spam for the whole batch, or <see langword="null" /> where the person named none; never beside <c>NotSpam</c>.</param>
/// <param name="StoredEmailIds">The messages, as a list row, a conversation, or a search published them, at most the batch bound this surface publishes.</param>
/// <remarks>
/// The identity makes a retried request the same request, exactly as on the other routes beside this one. The length
/// attributes publish the batch bound in the API document; nothing validates a request through them, so the route's own
/// refusal is what enforces it.
/// </remarks>
internal sealed record ClientMailSpamMarksRequest(
    string? RequestId,
    string? Verdict,
    string? SpamSubcategory,
    [property: MinLength(1), MaxLength(ClientMailMutationsEndpoint.MaximumChangesPerRequest)] IReadOnlyList<Guid>? StoredEmailIds);

/// <summary>What one batch of spam marks produced, one result per message it named.</summary>
/// <param name="Results">One entry per message, in the order the batch stated them.</param>
internal sealed record ClientMailSpamMarksResponse(IReadOnlyList<ClientMailSpamMarkResultResponse> Results);

/// <summary>What marking one message produced.</summary>
/// <param name="StoredEmailId">The message the entry answers for, as the request named it.</param>
/// <param name="Outcome">What happened, in the vocabulary every change on this surface is answered in.</param>
/// <param name="Moved">Whether the mark moved the message, which the account's switch for filing junk decides.</param>
/// <param name="Changes">One durable record per change written down for the mail server — a keyword change or the move — and empty where none was.</param>
internal sealed record ClientMailSpamMarkResultResponse(
    Guid StoredEmailId,
    string Outcome,
    bool Moved,
    IReadOnlyList<ClientMailRecordedChangeResponse> Changes)
{
    /// <summary>Describes what the use case answered about one message.</summary>
    /// <param name="result">What the use case answered.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    internal static ClientMailSpamMarkResultResponse For(SpamMarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ClientMailSpamMarkResultResponse(
            result.StoredEmailId.Value,
            result.Outcome switch
            {
                SpamMarkOutcome.Recorded => ClientMailChangeOutcomes.Recorded,
                SpamMarkOutcome.Applied => ClientMailChangeOutcomes.Applied,
                SpamMarkOutcome.MessageNotFound => ClientMailChangeOutcomes.MessageNotFound,
                SpamMarkOutcome.RequestIdAlreadyAskedForAnother => ClientMailChangeOutcomes.ChangeNotUsable,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(result),
                    result.Outcome,
                    "A spam mark outcome is one this surface publishes."),
            },
            result.IsMoved,
            [.. result.Records.Select(static record => new ClientMailRecordedChangeResponse(
                record.Request.Mutation.Name,
                record.Id.Value,
                record.Lifecycle.Name))]);
    }

    /// <summary>Describes a message the boundary could not name, before the use case was reached.</summary>
    /// <param name="storedEmailId">The identity the request carried.</param>
    /// <returns>The response entry.</returns>
    internal static ClientMailSpamMarkResultResponse MessageNotFound(Guid storedEmailId) =>
        new(storedEmailId, ClientMailChangeOutcomes.MessageNotFound, Moved: false, []);
}
