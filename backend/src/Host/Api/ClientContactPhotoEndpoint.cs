// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Portraits;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the photo of a person the signed-in user wrote down, and takes it back replaced or removed.</summary>
/// <remarks>
/// <para>
/// Three routes over one picture, under the contact it belongs to, which behave exactly as the routes over the user's
/// own portrait do: the same bound applied before a handler is entered, the same judgement of the kind from the octets
/// rather than from the declared content type, the same <c>204</c> for a contact with no photo, and the same served
/// response — private, revalidated against an entity tag, and never sniffed. <see cref="ClientPortraitEndpoint" />
/// holds the reasoning for each, and both surfaces call the same code for it.
/// </para>
/// <para>
/// The read is <see cref="MailFathomPermission.ContactsRead" /> and the two writes are
/// <see cref="MailFathomPermission.ContactsWrite" />, the grants every other route over the book asks for. Only a
/// contact of the user's own book carries a photo, so a write naming a collected contact, an organization's contact, or
/// nobody is answered <c>404</c>.
/// </para>
/// </remarks>
internal static class ClientContactPhotoEndpoint
{
    /// <summary>The route one contact's photo is read, replaced, and removed at, relative to the client prefix.</summary>
    internal const string ContactPhotoRoute = "/contacts/{contactId:guid}/photo";

    /// <summary>Maps the photo routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientContactPhoto(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ContactPhotoRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.ContactsRead)
            .Produces<Stream>(StatusCodes.Status200OK, PortraitImageType.Jpeg.MediaType, PortraitImageType.Png.MediaType);

        api.MapPost(ContactPhotoRoute, ReplaceAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ClientPortraitEndpoint.MaxPortraitBytes))
            .Accepts<Stream>(PortraitImageType.Jpeg.MediaType, PortraitImageType.Png.MediaType)
            .RequirePermission(MailFathomPermission.ContactsWrite);

        api.MapDelete(ContactPhotoRoute, RemoveAsync)
            .RequirePermission(MailFathomPermission.ContactsWrite);
    }

    /// <summary>Hands the caller the photo of one contact they see.</summary>
    /// <param name="contactId">The contact.</param>
    /// <param name="photos">The photos of the caller's contacts.</param>
    /// <param name="context">The request being answered, whose response carries the caching the client revalidates against.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the octets under the kind they are, <c>204</c> where the contact has no photo, or <c>400</c> for the empty identifier.</returns>
    internal static async Task<Results<FileContentHttpResult, NoContent, ProblemHttpResult>> ReadAsync(
        Guid contactId,
        [FromServices] ContactPhotos photos,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(context);

        if (!ContactEndpoints.TryReadContactId(contactId, out var identity))
        {
            return ContactEndpoints.EmptyIdentity();
        }

        return await photos.ReadAsync(identity, cancellationToken) is { } photo
            ? ClientPortraitEndpoint.Served(context, photo)
            : TypedResults.NoContent();
    }

    /// <summary>Replaces the photo of one contact of the caller's own book with the octets the request carries.</summary>
    /// <param name="contactId">The contact.</param>
    /// <param name="photos">The photos of the caller's contacts.</param>
    /// <param name="context">The request being answered, whose body carries the picture.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>204</c> when it was stored, <c>404</c> when the caller's own book holds no such contact, <c>413</c> for an upload over the bound, <c>415</c> for octets that are neither kind, or <c>400</c> for a request carrying no picture or naming the empty identifier.</returns>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> ReplaceAsync(
        Guid contactId,
        [FromServices] ContactPhotos photos,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(context);

        if (!ContactEndpoints.TryReadContactId(contactId, out var identity))
        {
            return ContactEndpoints.EmptyIdentity();
        }

        var upload = await ClientPortraitEndpoint.ReadUploadAsync(context, cancellationToken);

        if (upload.Portrait is not { } photo)
        {
            return upload.Refusal!;
        }

        return await photos.ReplaceAsync(identity, photo, cancellationToken)
            ? TypedResults.NoContent()
            : NoSuchContact();
    }

    /// <summary>Removes the photo of one contact of the caller's own book.</summary>
    /// <param name="contactId">The contact.</param>
    /// <param name="photos">The photos of the caller's contacts.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns><c>204</c>, whether or not the contact had a photo, <c>404</c> when the caller's own book holds no such contact, or <c>400</c> for the empty identifier.</returns>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> RemoveAsync(
        Guid contactId,
        [FromServices] ContactPhotos photos,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);

        if (!ContactEndpoints.TryReadContactId(contactId, out var identity))
        {
            return ContactEndpoints.EmptyIdentity();
        }

        return await photos.RemoveAsync(identity, cancellationToken)
            ? TypedResults.NoContent()
            : NoSuchContact();
    }

    private static NotFound<ProblemDetails> NoSuchContact() =>
        TypedResults.NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Detail = "Your own book holds no such contact. Only a person you wrote down there has a photo.",
        });
}
