// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.ReadSource;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one message's raw source — every header and every MIME part, as received — to the signed-in reader who asked for it.</summary>
/// <remarks>
/// <para>
/// It is asked for when the reader presses <em>Show source</em> and never with the message, so opening a message costs
/// nothing for a source nobody looks at. It speaks to no mail server, so it cannot fetch a message and cannot set the
/// remote <c>\Seen</c> flag.
/// </para>
/// <para>
/// The octets are served as <c>text/plain</c> with sniffing switched off, because they are sender-controlled bytes on
/// the deployment's own origin: a message carrying markup must be read as text rather than rendered as a page. A source
/// longer than <see cref="EmailSourceReader.MaximumServedOctets" /> is cut there, and
/// <see cref="TruncatedFromHeaderName" /> then states how many octets the whole message holds. <c>no-store</c> keeps it
/// out of every intermediary, and nothing about the request or the octets reaches a log.
/// </para>
/// </remarks>
internal static class ClientMailSourceEndpoint
{
    /// <summary>The route serving one message's source, relative to the client prefix.</summary>
    internal const string MailSourceRoute = "/messages/{storedEmailId:guid}/source";

    /// <summary>The header stating how many octets the whole message holds, present only where the source was cut.</summary>
    internal const string TruncatedFromHeaderName = "MailFathom-Source-Truncated-From";

    /// <summary>The media type the source is served as.</summary>
    internal const string SourceMediaType = "text/plain; charset=utf-8";

    /// <summary>The one thing a request for a source this deployment's screen withholds is told.</summary>
    internal const string ScreenedDetail =
        "This deployment screens the mail it serves, and a message's raw source cannot be screened, so it is not served.";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailSource(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailSourceRoute, ReadSourceAsync)
            .RequirePermission(MailFathomPermission.MailRead)
            .Produces<string>(StatusCodes.Status200OK, "text/plain");
    }

    /// <summary>Serves one of the acting user's messages as its raw source, or reports that there is none.</summary>
    /// <param name="storedEmailId">The message, as a list row or a conversation published it.</param>
    /// <param name="sources">Reads the source from the local copy, for a caller the read's own grant admits.</param>
    /// <param name="context">The request being answered, whose response the source is written to.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns>The source, <c>404</c> where this user has no source for such a message, <c>409</c> where this deployment's screen withholds it, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// A message this user does not hold, one no deployment ever held, and a local copy that is missing, damaged, or was
    /// never stored answer identically, so nothing here tells a caller what became of mail they cannot read.
    /// </remarks>
    internal static async Task<Results<EmptyHttpResult, NotFound, ProblemHttpResult>> ReadSourceAsync(
        [FromRoute] Guid storedEmailId,
        [FromServices] EmailSourceReader sources,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(context);

        if (storedEmailId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var outcome = await sources.ReadForReaderAsync(StoredEmailId.Create(storedEmailId), cancellationToken);

        if (outcome.Screened)
        {
            return ScreenedOut();
        }

        if (outcome.Octets is not { } octets)
        {
            return TypedResults.NotFound();
        }

        Describe(context.Response, outcome);

        await context.Response.Body.WriteAsync(octets, cancellationToken);

        return TypedResults.Empty;
    }

    /// <summary>Writes the headers that describe a source, before any octet of it is written.</summary>
    /// <param name="response">The response the source is written to.</param>
    /// <param name="outcome">The source that was read.</param>
    internal static void Describe(HttpResponse response, EmailSourceReadOutcome outcome)
    {
        response.ContentType = SourceMediaType;
        response.ContentLength = outcome.Octets?.Length;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "no-store";

        if (outcome.IsCut)
        {
            response.Headers[TruncatedFromHeaderName] = outcome.WholeOctetCount.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static ProblemHttpResult ScreenedOut() => TypedResults.Problem(
        ScreenedDetail,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [RouteAuthorization.ErrorCodeExtension] = MailFathomErrorCode.MessageSourceScreened.Value,
        });
}
