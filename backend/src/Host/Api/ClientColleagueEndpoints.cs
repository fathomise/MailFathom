// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Portraits;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in user the people of their organization: the directory, one colleague, their portrait, and keeping a copy of one.</summary>
/// <remarks>
/// <para>
/// The member directory is a projection of the organization's user records rather than a book anybody writes, so there
/// is no route here that changes a colleague: what a colleague is drawn as is what their record and an administrator
/// state. Keeping one differently is what the promotion is for, which copies them into the reader's own book.
/// </para>
/// <para>
/// A reader who belongs to no organization reads an empty directory and finds nobody, which is the state their
/// directory is in rather than a refusal. Reading is <see cref="MailFathomPermission.ContactsRead" /> and the
/// promotion is <see cref="MailFathomPermission.ContactsWrite" />, the grants every route over the books takes.
/// </para>
/// </remarks>
internal static class ClientColleagueEndpoints
{
    /// <summary>The route the reader's colleagues are listed at, relative to the client prefix.</summary>
    /// <remarks>A literal segment where the single-contact route takes an identifier, which routing prefers over a parameter.</remarks>
    internal const string ColleaguesRoute = "/contacts/colleagues";

    /// <summary>The route one colleague is read at, relative to the client prefix.</summary>
    internal const string ColleagueRoute = "/contacts/colleagues/{userId:guid}";

    /// <summary>The route one colleague's portrait is read at, relative to the client prefix.</summary>
    internal const string ColleaguePhotoRoute = "/contacts/colleagues/{userId:guid}/photo";

    /// <summary>The route a copy of one colleague is written into the reader's own book at, relative to the client prefix.</summary>
    internal const string ColleaguePromotionRoute = "/contacts/colleagues/{userId:guid}/promotion";

    /// <summary>Maps the colleague routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientColleagues(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ColleaguesRoute, ReadPageAsync)
            .RequirePermission(MailFathomPermission.ContactsRead);

        api.MapGet(ColleagueRoute, FindAsync)
            .RequirePermission(MailFathomPermission.ContactsRead);

        api.MapGet(ColleaguePhotoRoute, ReadPhotoAsync)
            .RequirePermission(MailFathomPermission.ContactsRead)
            .Produces<Stream>(StatusCodes.Status200OK, PortraitImageType.Jpeg.MediaType, PortraitImageType.Png.MediaType);

        api.MapPost(ColleaguePromotionRoute, PromoteAsync)
            .RequirePermission(MailFathomPermission.ContactsWrite);
    }

    /// <summary>Serves one bounded page of the reader's colleagues.</summary>
    /// <param name="search">Text a colleague must carry in their name or in one of their addresses, or <see langword="null" /> for everybody.</param>
    /// <param name="pageSize">How many colleagues the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="colleagues">Reads the page from the reader's organization.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>The bounds are the contact book's: the same default and ceiling, and the same bound on the search text.</remarks>
    internal static async Task<Results<Ok<ColleaguePageResponse>, ProblemHttpResult>> ReadPageAsync(
        [FromQuery] string? search,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] ColleagueBook colleagues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(colleagues);

        var request = new ContactPageRequest
        {
            Search = search,
            PageSize = pageSize,
            Cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor,
        };

        try
        {
            var page = await colleagues.ReadPageAsync(request, cancellationToken);

            return TypedResults.Ok(new ColleaguePageResponse(
                [.. page.Colleagues.Select(ColleagueResponse.For)],
                page.NextCursor?.Encode()));
        }
        catch (ContactQueryInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }
        catch (ContactCursorMalformedException refusal)
        {
            return Refuse(refusal.Message);
        }
    }

    /// <summary>Serves one colleague by their user identity.</summary>
    /// <param name="userId">The member to read.</param>
    /// <param name="colleagues">Reads the reader's organization.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the colleague, or <c>404</c> where the reader's organization holds no such person.</returns>
    /// <remarks>A user of another organization, the reader themselves, and nobody at all answer identically.</remarks>
    internal static async Task<Results<Ok<ColleagueResponse>, NotFound>> FindAsync(
        [FromRoute] Guid userId,
        [FromServices] ColleagueBook colleagues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(colleagues);

        if (NamedMember(userId) is not { } member)
        {
            return TypedResults.NotFound();
        }

        return await colleagues.FindAsync(member, cancellationToken) is { } colleague
            ? TypedResults.Ok(ColleagueResponse.For(colleague))
            : TypedResults.NotFound();
    }

    /// <summary>Hands the reader the portrait one colleague is drawn by.</summary>
    /// <param name="userId">The member whose portrait is read.</param>
    /// <param name="colleagues">Reads the reader's organization.</param>
    /// <param name="context">The request being answered, whose response carries the caching the client revalidates against.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the octets under the kind they are, or <c>404</c> where there is no such colleague or they have no portrait.</returns>
    internal static async Task<Results<FileContentHttpResult, NotFound>> ReadPhotoAsync(
        [FromRoute] Guid userId,
        [FromServices] ColleagueBook colleagues,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(colleagues);
        ArgumentNullException.ThrowIfNull(context);

        if (NamedMember(userId) is not { } member)
        {
            return TypedResults.NotFound();
        }

        return await colleagues.ReadPortraitAsync(member, cancellationToken) is { } portrait
            ? ClientPortraitEndpoint.Served(context, portrait)
            : TypedResults.NotFound();
    }

    /// <summary>Writes a copy of one colleague into the reader's own book.</summary>
    /// <param name="userId">The member to copy.</param>
    /// <param name="colleagues">Performs the write.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome and no record, or <c>400</c> where the colleague has no address a contact could carry.</returns>
    /// <remarks>Answered as the contact promotion is, with the outcome alone, because the caller stated an identity rather than a record.</remarks>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> PromoteAsync(
        [FromRoute] Guid userId,
        [FromServices] ColleagueBook colleagues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(colleagues);

        if (NamedMember(userId) is not { } member)
        {
            return TypedResults.Ok(ContactWriteResponse.OutcomeOf(ContactWriteResult.NotFound()));
        }

        try
        {
            return TypedResults.Ok(
                ContactWriteResponse.OutcomeOf(await colleagues.PromoteAsync(member, ContactAddressWriterKind.Person, cancellationToken)));
        }
        catch (ContactRecordInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }
    }

    /// <summary>Reads the member a route named, refusing the one value a UUID route constraint still admits.</summary>
    private static UserId? NamedMember(Guid userId) =>
        userId == Guid.Empty ? null : UserId.Create(userId);

    /// <summary>States what a caller has to change, without echoing anything about the person it was reading.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}
