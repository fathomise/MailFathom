// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Photos;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Answers which photo each of a set of addresses is drawn by, for a surface whose own response names people by address alone.</summary>
/// <remarks>
/// <para>
/// A mail list, a search, a conversation, and a message carry each sender's photo reference themselves, because the read
/// that serves them resolves it beside the rows. An AI answer does not: its blocks are composed once and stored, and
/// reach a client through the run's events and the conversation's record, so the people a block names are resolved here
/// when the block is drawn — one request for a whole block, never one per person — and against the books as they stand
/// then, so a person written down after the answer was composed is drawn by the photo they have now.
/// </para>
/// <para>
/// The addresses travel in the request body rather than the query string, because a list of correspondents' addresses
/// is personal data and a query string is what an access log writes down. An address that names no mailbox is answered
/// as one drawn by initials rather than refused: what is drawn is a block a model wrote, not something the caller typed.
/// </para>
/// <para>
/// The read is <see cref="MailFathomPermission.ContactsRead" />, the grant both photo routes take, so nothing is named
/// here that the caller could not then read.
/// </para>
/// </remarks>
internal static class ClientPersonPhotoEndpoint
{
    /// <summary>The route the photos of a set of addresses are resolved at, relative to the client prefix.</summary>
    /// <remarks>A literal segment where the single-contact routes take an identifier, which routing prefers over a parameter.</remarks>
    internal const string PersonPhotosRoute = "/contacts/photos";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientPersonPhotos(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapPost(PersonPhotosRoute, ResolveAsync)
            .RequirePermission(MailFathomPermission.ContactsRead);
    }

    /// <summary>Resolves the photo each address is drawn by.</summary>
    /// <param name="request">The addresses to resolve.</param>
    /// <param name="photos">Resolves them against the caller's books and organization.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with an entry for every address drawn by a photo, or <c>400</c> for a request naming no list or more addresses than one resolution answers.</returns>
    internal static async Task<Results<Ok<PersonPhotosResponse>, ProblemHttpResult>> ResolveAsync(
        [FromBody] PersonPhotosRequest? request,
        [FromServices] PersonPhotos photos,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);

        if (request?.Addresses is not { } written)
        {
            return Refuse("The request names the addresses to resolve.");
        }

        if (written.Count > PersonPhotos.MaximumAddresses)
        {
            return Refuse($"One request resolves at most {PersonPhotos.MaximumAddresses} addresses.");
        }

        var addresses = PersonPhotoResponse.AddressesOf(written);
        var resolved = await photos.ResolveAsync(addresses, cancellationToken);

        return TypedResults.Ok(new PersonPhotosResponse(
        [
            .. addresses
                .Select(address => (address, photo: PersonPhotoResponse.For(address, resolved)))
                .Where(entry => entry.photo is not null)
                .Select(entry => new AddressedPersonPhotoResponse(entry.address.Address, entry.photo!)),
        ]));
    }

    private static ProblemHttpResult Refuse(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>The addresses whose photos a caller asks for.</summary>
/// <param name="Addresses">The addresses, as the surface drawing them carries them.</param>
internal sealed record PersonPhotosRequest(IReadOnlyList<string?>? Addresses);

/// <summary>The photos a set of addresses is drawn by.</summary>
/// <param name="Photos">One entry for every address drawn by a photo; an address drawn by initials is absent.</param>
internal sealed record PersonPhotosResponse(IReadOnlyList<AddressedPersonPhotoResponse> Photos);

/// <summary>One address and the photo the person at it is drawn by.</summary>
/// <param name="Address">The address, as the request wrote it, so a caller finds the entry under the spelling it sent.</param>
/// <param name="Photo">The photo.</param>
internal sealed record AddressedPersonPhotoResponse(string Address, PersonPhotoResponse Photo);
