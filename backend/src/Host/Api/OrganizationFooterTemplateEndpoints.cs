// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Maps the administrative routes an organization's footer template is set, read, and cleared at.</summary>
/// <remarks>
/// Reading is <see cref="MailFathomPermission.AdminRead" /> and writing is
/// <see cref="MailFathomPermission.AdminConfigurationWrite" />, each at a scope covering the organization the route names.
/// </remarks>
internal static class OrganizationFooterTemplateEndpoints
{
    /// <summary>The route the template is read, written, and cleared at.</summary>
    internal const string FooterTemplateRoute = $"{OrganizationEndpoints.OrganizationRoute}/footer-template";

    /// <summary>Maps the routes.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapOrganizationFooterTemplates(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(FooterTemplateRoute, ReadAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        api.MapPut(FooterTemplateRoute, SaveAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MailFooterRequest.MaxRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminConfigurationWrite);

        api.MapDelete(FooterTemplateRoute, ClearAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminConfigurationWrite);
    }

    /// <summary>Reads an organization's template.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="templates">The templates.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the template, which is absent where none is set.</returns>
    internal static async Task<Ok<OrganizationFooterTemplateResponse>> ReadAsync(
        Guid organizationId,
        [FromServices] OrganizationMailFooterTemplates templates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templates);

        var template = await templates.ReadAsync(organizationId, cancellationToken);

        return TypedResults.Ok(new OrganizationFooterTemplateResponse(
            template is null ? null : MailFooterResponse.For(template)));
    }

    /// <summary>Sets an organization's template, which every member's message from its accounts then carries.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="request">The whole template, its images included, naming its variables as <c>{{name}}</c>.</param>
    /// <param name="templates">The templates.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with the template as saved, its markup sanitized; <c>404</c> when no such organization is held; or <c>400</c> naming what was wrong with it, an unknown variable by its name.</returns>
    internal static async Task<Results<Ok<MailFooterResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> SaveAsync(
        Guid organizationId,
        [FromBody] MailFooterRequest request,
        [FromServices] OrganizationMailFooterTemplates templates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(templates);

        if (request.Stated(out var malformed) is not { } images)
        {
            return TypedResults.Problem(malformed, statusCode: StatusCodes.Status400BadRequest);
        }

        var write = await templates.SaveAsync(organizationId, request.Html!, images, cancellationToken);

        return write.Outcome switch
        {
            MailFooterWriteOutcome.Saved => TypedResults.Ok(MailFooterResponse.For(write.Saved!)),
            MailFooterWriteOutcome.Refused => TypedResults.Problem(
                MailFooterRefusals.Describe(write.Refusal!),
                statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = $"This deployment holds no organization {organizationId:D}.",
            }),
        };
    }

    /// <summary>Clears an organization's template, so each member's own footer is what they send with again.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="templates">The templates.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>204</c> whether or not one was set.</returns>
    internal static async Task<NoContent> ClearAsync(
        Guid organizationId,
        [FromServices] OrganizationMailFooterTemplates templates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templates);

        await templates.ClearAsync(organizationId, cancellationToken);

        return TypedResults.NoContent();
    }
}

/// <summary>An organization's footer template, as the administrative endpoint reads it.</summary>
/// <param name="Template">The template, or <see langword="null" /> where none is set.</param>
internal sealed record OrganizationFooterTemplateResponse(MailFooterResponse? Template);
