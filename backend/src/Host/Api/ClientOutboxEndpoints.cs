// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves what the signed-in user does about each of their accounts' outboxes: read one, and decide about one send in it.</summary>
/// <remarks>
/// <para>
/// It is what a client draws after a message has been sent. Sending queues, so the send the user just asked for has
/// not left yet, and an account's outbox is where the screen watches it go — and where a send that is still queued is
/// taken back, one that failed is offered another chance, and one that failed for good is dismissed. The outbox holds
/// what has not left and what was refused; a delivered send has left it, and a withdrawn or dismissed one is gone from
/// it. It is read-only as a mailbox: it is no folder, so no route that moves, copies, or flags a message can name it,
/// and a send in it is named by the identifier it answered with rather than as a stored email.
/// </para>
/// <para>
/// <b>A listing names an account and never the deployment.</b> The narrowing is required rather than optional, so
/// there is no unnarrowed reading here at all: one that fell back to every account would page through every user's
/// outgoing mail, which is the deployment-wide catalog a user-facing surface must never compose. An account another
/// user is assigned is refused exactly as one nobody configured, and a send another user made answers exactly as one
/// nobody made.
/// </para>
/// <para>
/// A page and a preview carry mail — who a send is addressed to, its subject, and the message itself — so they are read
/// under both <c>mailfathom.mail.read</c> and <c>mailfathom.mail.send</c>, and a caller holding only one of them is
/// refused the page. A send-only credential still reads one send by identity, as the stage, the attempts, and each
/// recipient's outcome without the subject or the message, because that is what watching its own send needs.
/// </para>
/// <para>
/// Every decision here is <c>mailfathom.mail.send</c>. Withdrawing, offering again, and dismissing a send are part of
/// sending rather than powers beside it, so a credential granted to read a mailbox decides nothing here.
/// </para>
/// </remarks>
internal static class ClientOutboxEndpoints
{
    /// <summary>The route one page of the user's outbox is read from, relative to the client prefix.</summary>
    internal const string OutboxRoute = "/outbox";

    /// <summary>The route one of the user's sends is read from.</summary>
    internal const string OutboxSendRoute = $"{OutboxRoute}/{{outgoingEmailId:guid}}";

    /// <summary>The route one send is withdrawn on.</summary>
    internal const string OutboxCancellationRoute = $"{OutboxRoute}/cancellation";

    /// <summary>The route one send is offered again on.</summary>
    /// <remarks>
    /// A path of its own rather than a field on the cancellation request, for the reason the administrative pair are
    /// two paths: they are opposite decisions, and a body carrying which one was meant would make a mistyped value the
    /// difference between withdrawing a message and sending it a second time.
    /// </remarks>
    internal const string OutboxRequeueRoute = $"{OutboxRoute}/requeue";

    /// <summary>The route one send is dismissed from its account's outbox on.</summary>
    internal const string OutboxDismissalRoute = $"{OutboxRoute}/dismissal";

    /// <summary>Maps the outbox routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientOutbox(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(OutboxRoute, ReadPageAsync)
            .RequirePermission(MailFathomPermission.MailSend);

        api.MapGet(OutboxSendRoute, ReadSendAsync)
            .RequirePermission(MailFathomPermission.MailSend);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the administrative
        // decisions reach it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the
        // request body feature, so a body over the bound is answered 413 before the handler is reached.
        api.MapPost(OutboxCancellationRoute, CancelAsync)
            .WithMetadata(new RequestSizeLimitAttribute(OutboxEndpoints.MaxDecisionRequestBytes))
            .RequirePermission(MailFathomPermission.MailSend);

        api.MapPost(OutboxRequeueRoute, RequeueAsync)
            .WithMetadata(new RequestSizeLimitAttribute(OutboxEndpoints.MaxDecisionRequestBytes))
            .RequirePermission(MailFathomPermission.MailSend);

        api.MapPost(OutboxDismissalRoute, DismissAsync)
            .WithMetadata(new RequestSizeLimitAttribute(OutboxEndpoints.MaxDecisionRequestBytes))
            .RequirePermission(MailFathomPermission.MailSend);
    }

    /// <summary>Serves one page of the named account's outbox, newest first.</summary>
    /// <param name="account">The account to read, by its identifier or its display name, which is required rather than optional.</param>
    /// <param name="stage">The stage among the three the outbox holds to narrow to, or <see langword="null" /> for all three.</param>
    /// <param name="pageSize">How many sends the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="outbox">Reads the page, for the user the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, <c>400</c> naming what was wrong with the request, or <c>403</c> for a caller that does not hold both <c>mailfathom.mail.read</c> and <c>mailfathom.mail.send</c>.</returns>
    /// <remarks>
    /// An account this user is not assigned is <c>400</c> rather than <c>404</c>, which is how every other narrowing on
    /// this surface answers one: it is a mistake in the request the client wrote rather than a missing resource, and it
    /// answers identically for an account nobody configured so that nothing here reports whose accounts exist.
    /// </remarks>
    internal static async Task<Results<Ok<ClientOutboxPageResponse>, ProblemHttpResult>> ReadPageAsync(
        [FromQuery] string? account,
        [FromQuery] string? stage,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] UserOutbox outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (NamedAccount(account) is not { } named)
        {
            return Refuse("A reading of an outbox names the account it is reading, which this one named none of.");
        }

        if (!TryReadStage(stage, out var namedStage))
        {
            return Refuse(UnknownStage());
        }

        OutboxCursor? decodedCursor = null;

        if (!string.IsNullOrWhiteSpace(cursor) && !OutboxCursor.TryDecode(cursor, out decodedCursor))
        {
            return Refuse("The continuation cursor is not one this deployment issued.");
        }

        try
        {
            var result = await outbox.ReadPageAsync(
                named,
                namedStage,
                pageSize,
                decodedCursor,
                cancellationToken);

            return result.Page is { } page
                ? TypedResults.Ok(ClientOutboxPageResponse.For(page))
                : Refuse(DescribeRefusal(result.Outcome));
        }
        catch (MailAccountNotAccessibleException)
        {
            return Refuse("The account is not one this user is assigned.");
        }
    }

    /// <summary>Serves one of the acting user's sends: as the message it is to a caller that may read the outbox, and as its delivery to one that may only send.</summary>
    /// <param name="outgoingEmailId">The send to read.</param>
    /// <param name="outbox">Reads the send, for the user the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the preview or the delivery, or <c>404</c> where this user has no send under that identifier.</returns>
    /// <remarks>
    /// A send another user made and one nobody made answer identically. This route addresses one record by identity,
    /// so its absence is the absence of the thing addressed — unlike the decisions beside it, which address the outbox
    /// and report what became of the send they named.
    /// </remarks>
    internal static async Task<Results<Ok<ClientOutboxPreviewResponse>, Ok<OutboxSendResponse>, NotFound>> ReadSendAsync(
        [FromRoute] Guid outgoingEmailId,
        [FromServices] UserOutbox outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (NamedSend(outgoingEmailId) is not { } identifier)
        {
            return TypedResults.NotFound();
        }

        if (outbox.MayReadOutbox)
        {
            return await outbox.ReadPreviewAsync(identifier, cancellationToken) is { } preview
                ? TypedResults.Ok(ClientOutboxPreviewResponse.For(preview))
                : TypedResults.NotFound();
        }

        return await outbox.FindAsync(identifier, cancellationToken) is { } record
            ? TypedResults.Ok(OutboxSendResponse.For(record))
            : TypedResults.NotFound();
    }

    /// <summary>Withdraws one of the acting user's sends that has not begun transmitting.</summary>
    /// <param name="request">The send to cancel.</param>
    /// <param name="outbox">Performs the decision, for the user the credential names.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with what happened, or <c>400</c> where the request named no send.</returns>
    /// <remarks>
    /// A send that cannot be withdrawn is an outcome rather than a refusal, and so is one this user does not hold:
    /// the caller asked a question this deployment can answer, and being told the message has gone past the point of
    /// recall is exactly what somebody acting on a screen a moment old needs.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxDecisionResponse>, ProblemHttpResult>> CancelAsync(
        [FromBody] OutboxCancellationRequest? request,
        [FromServices] UserOutbox outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (NamedSend(request?.OutgoingEmail) is not { } identifier)
        {
            return Refuse(NoSendNamed());
        }

        var outcome = await outbox.CancelAsync(identifier, cancellationToken);

        return TypedResults.Ok(OutboxDecisionResponse.For(identifier, outcome));
    }

    /// <summary>Offers one of the acting user's sends again, which is the decision this system will not take on its own.</summary>
    /// <param name="request">The send to offer again, and whether a permanent refusal was restated.</param>
    /// <param name="outbox">Performs the decision, for the user the credential names.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with what happened, or <c>400</c> where the request named no send.</returns>
    /// <remarks>
    /// It names one send and never a set, for the reason the administrative decision does: a message whose outcome
    /// nobody knows may already be in somebody's mailbox, so a filtered re-queue would be an unknown number of
    /// duplicates asked for in one request.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxDecisionResponse>, ProblemHttpResult>> RequeueAsync(
        [FromBody] OutboxRequeueRequest? request,
        [FromServices] UserOutbox outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (NamedSend(request?.OutgoingEmail) is not { } identifier)
        {
            return Refuse(NoSendNamed());
        }

        var outcome = await outbox.RequeueAsync(
            identifier,
            request?.RefusalRestated ?? false,
            cancellationToken);

        return TypedResults.Ok(OutboxDecisionResponse.For(identifier, outcome));
    }

    /// <summary>Takes one of the acting user's refused sends, or one whose outcome nobody will learn, out of its account's outbox.</summary>
    /// <param name="request">The send to dismiss.</param>
    /// <param name="outbox">Performs the decision, for the user the credential names.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with what happened, or <c>400</c> where the request named no send.</returns>
    /// <remarks>
    /// It is offered at <c>Refused</c>, and at <c>TransmissionBegun</c> once no attempt holds the send. The stage is
    /// unchanged by it: a dismissal records that the person accepted the failure, not that the message was withdrawn.
    /// </remarks>
    internal static async Task<Results<Ok<OutboxDecisionResponse>, ProblemHttpResult>> DismissAsync(
        [FromBody] OutboxDismissalRequest? request,
        [FromServices] UserOutbox outbox,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        if (NamedSend(request?.OutgoingEmail) is not { } identifier)
        {
            return Refuse(NoSendNamed());
        }

        var outcome = await outbox.DismissAsync(identifier, cancellationToken);

        return TypedResults.Ok(OutboxDecisionResponse.For(identifier, outcome));
    }

    /// <summary>Reads the account a request narrows to, refusing text no name of this system is spelled with.</summary>
    private static MailAccountSelector? NamedAccount(string? account)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        try
        {
            return MailAccountSelector.Create(account);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads the stage a request narrows to, taking every stage where it names none.</summary>
    private static bool TryReadStage(string? stage, out OutgoingEmailStage? named)
    {
        named = null;

        if (string.IsNullOrWhiteSpace(stage))
        {
            return true;
        }

        if (!Enum.TryParse<OutgoingEmailStage>(stage, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return false;
        }

        named = parsed;

        return true;
    }

    /// <summary>Reads the send a request named, keeping an absent identifier apart from an unusable one.</summary>
    private static OutgoingEmailId? NamedSend(Guid? outgoingEmail) =>
        outgoingEmail is { } identifier && identifier != Guid.Empty
            ? OutgoingEmailId.Create(identifier)
            : null;

    /// <summary>States what a caller has to change, without restating the filters they already sent.</summary>
    private static string DescribeRefusal(OutboxQueryOutcome outcome) => outcome switch
    {
        OutboxQueryOutcome.PageSizeOutOfRange =>
            $"A page of the outbox holds between 1 and {OutboxQuery.MaximumPageSize} records.",
        OutboxQueryOutcome.StageUnknown => UnknownStage(),
        _ => "The continuation cursor was issued for a different set of outbox filters.",
    };

    /// <summary>States that the request named no stage a send in the outbox stands at.</summary>
    private static string UnknownStage() =>
        "The stage filter names no stage a message in the outbox stands at. It is one of "
        + $"{string.Join(", ", OutgoingEmailRecord.OutboxStages)}.";

    /// <summary>States that the request named no send to decide about.</summary>
    private static string NoSendNamed() =>
        "The request named no queued message. Name the identifier the outbox reading reports for it.";

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}
