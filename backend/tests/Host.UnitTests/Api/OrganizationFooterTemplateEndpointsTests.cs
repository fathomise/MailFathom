// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

public sealed class OrganizationFooterTemplateEndpointsTests
{
    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000aa");

    private readonly InMemoryMailFooterStore store =
        new InMemoryMailFooterStore().Holding(Organization, MailAccountId.Create("work"));

    /// <summary>A template naming a variable nobody fills is refused naming it, so the administrator sees the typo.</summary>
    [Fact]
    public async Task SaveAsync_ATemplateNamingAnUnknownVariable_AnswersBadRequestNamingIt()
    {
        // Act
        var result = await OrganizationFooterTemplateEndpoints.SaveAsync(
            Organization,
            new MailFooterRequest("<p>{{displayName}} {{department}}</p>"),
            this.Templates(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("'department'", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
    }

    /// <summary>A template set, read, and cleared through the routes ends where it started.</summary>
    [Fact]
    public async Task Handlers_SetReadAndClear_RoundTripTheTemplate()
    {
        // Arrange
        var templates = this.Templates();

        // Act
        var saved = await OrganizationFooterTemplateEndpoints.SaveAsync(
            Organization,
            new MailFooterRequest("<p>{{displayName}}</p>"),
            templates,
            TestContext.Current.CancellationToken);
        var read = await OrganizationFooterTemplateEndpoints.ReadAsync(Organization, templates, TestContext.Current.CancellationToken);
        await OrganizationFooterTemplateEndpoints.ClearAsync(Organization, templates, TestContext.Current.CancellationToken);
        var cleared = await OrganizationFooterTemplateEndpoints.ReadAsync(Organization, templates, TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<MailFooterResponse>>(saved.Result);
        Assert.Equal("<p>{{displayName}}</p>", read.Value!.Template?.Html);
        Assert.Null(cleared.Value!.Template);
    }

    /// <summary>An organization the deployment does not hold is answered as absent.</summary>
    [Fact]
    public async Task SaveAsync_AnOrganizationNotHeld_AnswersNotFound()
    {
        // Act
        var result = await OrganizationFooterTemplateEndpoints.SaveAsync(
            new Guid("0198f0aa-0000-7000-8000-0000000000bb"),
            new MailFooterRequest("<p>{{displayName}}</p>"),
            this.Templates(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    private OrganizationMailFooterTemplates Templates() =>
        new(
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead, MailFathomPermission.AdminConfigurationWrite),
            this.store,
            new MailFootings.VerbatimMailFooterMarkup());
}
