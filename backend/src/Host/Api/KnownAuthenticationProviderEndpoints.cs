// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.KnownAuthenticationProviders;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Lists, adds, and removes the providers whose receiving server an account believes by default.</summary>
/// <remarks>
/// Every route is held over the deployment alone, for the reasons <see cref="KnownAuthenticationProviderAdministration" />
/// gives. A refusal names the half that was wrong and never echoes the value, which is a host name.
/// </remarks>
internal static class KnownAuthenticationProviderEndpoints
{
    /// <summary>The route the list is read and added to at.</summary>
    internal const string KnownAuthenticationProvidersRoute = "/known-authentication-providers";

    /// <summary>The route one host's entry is removed at.</summary>
    internal const string KnownAuthenticationProviderRoute = $"{KnownAuthenticationProvidersRoute}/{{imapHost}}";

    /// <summary>The greatest request body the addition reads before refusing it.</summary>
    internal const int MaxRequestBytes = 4 * 1024;

    /// <summary>Maps the known-provider routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapKnownAuthenticationProviders(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(KnownAuthenticationProvidersRoute, ListAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(KnownAuthenticationProvidersRoute, AddAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapDelete(KnownAuthenticationProviderRoute, RemoveAsync)
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);
    }

    /// <summary>Lists one page of the known providers, in the order they were added.</summary>
    /// <param name="pageSize">How many entries the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="providers">The known-provider administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page's entries and the cursor the following page is asked with, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<KnownAuthenticationProviderListResponse>, ProblemHttpResult>> ListAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] KnownAuthenticationProviderAdministration providers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);

        if (!AdminListingRequest.TryResolve(
                AdministrativeListing.KnownAuthenticationProviders,
                pageSize,
                cursor,
                out var query,
                out var refusal))
        {
            return refusal;
        }

        var listing = await providers.ReadAsync(query, cancellationToken);

        return TypedResults.Ok(new KnownAuthenticationProviderListResponse(
            [.. listing.Entries.Select(KnownAuthenticationProviderResponse.For)],
            AdminListingRequest.NextCursor(AdministrativeListing.KnownAuthenticationProviders, listing.ContinuesAfter)));
    }

    /// <summary>Adds an entry.</summary>
    /// <param name="request">The IMAP host and the authserv-id.</param>
    /// <param name="providers">The known-provider administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the entry as it is matched, <c>409</c> when the list already names a provider for the host, or <c>400</c> naming the half that cannot be matched.</returns>
    internal static async Task<Results<Ok<KnownAuthenticationProviderAddedResponse>, ProblemHttpResult>> AddAsync(
        [FromBody] KnownAuthenticationProviderRequest? request,
        [FromServices] KnownAuthenticationProviderAdministration providers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);

        if (KnownAuthenticationProvider.TryNormalizeImapHost(request?.ImapHost) is not { } imapHost)
        {
            return MalformedHost();
        }

        if (KnownAuthenticationProvider.TryNormalizeAuthorityIdentifier(request!.AuthorityIdentifier) is not { } authorityIdentifier)
        {
            return Refused($"The authserv-id must be a domain-shaped token of at most {TrustedAuthenticationAuthority.MaximumLength} characters with no whitespace.");
        }

        var provider = new KnownAuthenticationProvider(imapHost, authorityIdentifier);

        return await providers.AddAsync(provider, cancellationToken)
            ? TypedResults.Ok(new KnownAuthenticationProviderAddedResponse(provider.ImapHost, provider.AuthorityIdentifier))
            : TypedResults.Problem(
                "The list already names a provider for this IMAP host. Remove that entry first to replace it.",
                statusCode: StatusCodes.Status409Conflict);
    }

    /// <summary>Removes the entry for one host.</summary>
    /// <param name="imapHost">The IMAP host.</param>
    /// <param name="providers">The known-provider administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it is removed, <c>404</c> when the list names no provider for the host, or <c>400</c> when the host cannot be matched.</returns>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> RemoveAsync(
        string imapHost,
        [FromServices] KnownAuthenticationProviderAdministration providers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);

        if (KnownAuthenticationProvider.TryNormalizeImapHost(imapHost) is not { } host)
        {
            return MalformedHost();
        }

        return await providers.RemoveAsync(host, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "The list names no provider for this IMAP host. List the known providers to read the hosts it does name.",
            });
    }

    private static ProblemHttpResult MalformedHost() =>
        Refused("The IMAP host must be a host name or an address, with no port, wildcard, path, or whitespace.");

    private static ProblemHttpResult Refused(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
