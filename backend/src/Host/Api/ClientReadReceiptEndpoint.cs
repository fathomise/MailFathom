// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Receipts;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Maps the route a reader sends the read receipt a received message asked for through.</summary>
/// <remarks>
/// It is the only way a receipt leaves this deployment, and it takes no body: what is sent, to whom, and in which words is
/// decided by the message and the reader's language, so a request can only ask for the one receipt the message asked for.
/// </remarks>
internal static class ClientReadReceiptEndpoint
{
    /// <summary>The route of the receipt one message is answered with.</summary>
    internal const string ReadReceiptRoute = "/messages/{storedEmailId:guid}/read-receipt";

    /// <summary>The extension a refusal names its reason in, as the outcome's own name.</summary>
    internal const string ReasonExtension = "reason";

    /// <summary>The extension a refusal for a receipt already sent carries that receipt's time in.</summary>
    internal const string SentAtExtension = "sentAt";

    /// <summary>Maps the route onto the client API.</summary>
    /// <param name="api">The client API's route group.</param>
    internal static void MapClientReadReceipt(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapPost(ReadReceiptRoute, SendAsync)
            .RequirePermission(MailFathomPermission.MailSend);
    }

    /// <summary>Sends the read receipt one message asked for, once.</summary>
    /// <param name="storedEmailId">The message being answered.</param>
    /// <param name="sending">The use case that sends it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The time the receipt was sent, or why none was.</returns>
    /// <remarks>
    /// A message this user does not read is <c>404</c>, answered the same whether it exists or not. Every other refusal
    /// is <c>409</c>, because nothing about the request is wrong and no rewriting of it would help: the message asked for
    /// nothing, is spam, is the account's own, or was answered already — in which case <c>sentAt</c> says when.
    /// </remarks>
    internal static async Task<Results<Ok<ClientReadReceiptSentResponse>, ProblemHttpResult>> SendAsync(
        [FromRoute] Guid storedEmailId,
        [FromServices] ReadReceiptSending sending,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sending);

        if (storedEmailId == Guid.Empty)
        {
            return Refused(ReadReceiptSendingOutcome.NotFound, sentAt: null);
        }

        try
        {
            var result = await sending.SendAsync(StoredEmailId.Create(storedEmailId), cancellationToken);

            return result.Outcome is ReadReceiptSendingOutcome.Sent
                ? TypedResults.Ok(new ClientReadReceiptSentResponse(result.SentAt!.Value))
                : Refused(result.Outcome, result.SentAt);
        }
        catch (OutgoingMailRefusedException refusal)
        {
            return Coded(refusal, StatusCodes.Status409Conflict);
        }
        catch (SensitiveContentScannerUnavailableException refusal)
        {
            return Coded(refusal, StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static ProblemHttpResult Refused(ReadReceiptSendingOutcome outcome, DateTimeOffset? sentAt)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ReasonExtension] = outcome.ToString(),
        };

        if (sentAt is { } earlier)
        {
            extensions[SentAtExtension] = earlier;
        }

        return TypedResults.Problem(
            DetailOf(outcome),
            statusCode: outcome is ReadReceiptSendingOutcome.NotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict,
            extensions: extensions);
    }

    private static string DetailOf(ReadReceiptSendingOutcome outcome) => outcome switch
    {
        ReadReceiptSendingOutcome.NotFound => "No message by that identifier is one you read.",
        ReadReceiptSendingOutcome.NotRequested => "The message asked for no read receipt.",
        ReadReceiptSendingOutcome.Spam => "The message is spam, and answering it would tell its sender the address reads mail.",
        ReadReceiptSendingOutcome.OwnMessage => "The message is your account's own.",
        ReadReceiptSendingOutcome.AlreadySent => "A read receipt was already sent for the message.",
        ReadReceiptSendingOutcome.SenderUnconfigured => "The account configures no address to send from.",
        _ => "No receipt can be sent to the address the message named.",
    };

    private static ProblemHttpResult Coded(MailFathomException refusal, int statusCode) => TypedResults.Problem(
        refusal.Message,
        statusCode: statusCode,
        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [RouteAuthorization.ErrorCodeExtension] = refusal.ErrorCode.Value,
        });
}

/// <summary>Says when the read receipt a reader asked for was sent.</summary>
/// <param name="SentAt">When it was written down to be sent, which is the time every later reading of the message reports.</param>
internal sealed record ClientReadReceiptSentResponse(DateTimeOffset SentAt);
