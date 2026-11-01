// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Footers;

public sealed class OrganizationMailFooterTemplatesTests
{
    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000aa");

    private static readonly Guid UnknownOrganization = new("0198f0aa-0000-7000-8000-0000000000bb");

    private readonly InMemoryMailFooterStore store =
        new InMemoryMailFooterStore().Holding(Organization, MailAccountId.Create("work"));

    [Fact]
    public async Task SaveAsync_ATemplateNamingOnlyKnownVariables_IsSavedAndReadBack()
    {
        // Arrange
        var templates = this.Templates(MailFathomPermission.AdminConfigurationWrite, MailFathomPermission.AdminRead);

        // Act
        var write = await templates.SaveAsync(Organization, "<p>{{displayName}} · {{jobTitle}}</p>", [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFooterWriteOutcome.Saved, write.Outcome);
        Assert.Equal("<p>{{displayName}} · {{jobTitle}}</p>", (await templates.ReadAsync(Organization, TestContext.Current.CancellationToken))?.Html);
    }

    [Fact]
    public async Task SaveAsync_ATemplateNamingAnUnknownVariable_IsRefusedNamingIt()
    {
        // Arrange
        var templates = this.Templates(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var write = await templates.SaveAsync(Organization, "<p>{{displayName}} {{department}}</p>", [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFooterWriteOutcome.Refused, write.Outcome);
        Assert.Equal(new MailFooterRefusal(MailFooterRefusalReason.VariableUnknown, "department"), write.Refusal);
        Assert.Null(await this.store.ReadTemplateAsync(Organization, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_AnOrganizationThatDoesNotExist_ReportsItUnknown()
    {
        // Arrange
        var templates = this.Templates(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var write = await templates.SaveAsync(UnknownOrganization, "<p>{{displayName}}</p>", [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFooterWriteOutcome.OrganizationUnknown, write.Outcome);
    }

    [Fact]
    public async Task SaveAsync_ACallerWithoutTheConfigurationWriteGrant_IsRefused()
    {
        // Arrange
        var templates = this.Templates(MailFathomPermission.AdminRead);

        // Act
        var save = () => templates.SaveAsync(Organization, "<p>{{displayName}}</p>", [], TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(save);
    }

    [Fact]
    public async Task ClearAsync_ASavedTemplate_IsGone()
    {
        // Arrange
        var templates = this.Templates(MailFathomPermission.AdminConfigurationWrite, MailFathomPermission.AdminRead);
        await templates.SaveAsync(Organization, "<p>{{displayName}}</p>", [], TestContext.Current.CancellationToken);

        // Act
        await templates.ClearAsync(Organization, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(await templates.ReadAsync(Organization, TestContext.Current.CancellationToken));
    }

    private OrganizationMailFooterTemplates Templates(params MailFathomPermission[] granted) =>
        new(
            AccessAuthorizations.ForAdministratorGranted(granted),
            this.store,
            new MailFootings.VerbatimMailFooterMarkup());
}
