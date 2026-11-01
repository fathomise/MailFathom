// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

public sealed class ClientMailFooterEndpointsTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private static readonly MailAccountId Held = MailAccountId.Create("held");

    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000aa");

    private readonly InMemoryMailFooterStore store = new InMemoryMailFooterStore()
        .Assigning(SyntheticUser.Deployment, Primary)
        .Assigning(SyntheticUser.Deployment, Held)
        .Holding(Organization, Held);

    /// <summary>The paths a client composes.</summary>
    [Fact]
    public void Routes_AreThePathsAClientComposes()
    {
        // Assert
        Assert.Equal("/mail-footers", ClientMailFooterEndpoints.MailFootersRoute);
        Assert.Equal("/accounts/{accountId}/footer", ClientMailFooterEndpoints.OwnMailFooterRoute);
    }

    /// <summary>What was saved is what the client is handed back, and reading it back returns the same.</summary>
    [Fact]
    public async Task SaveOwnAsync_AFooterOnTheirOwnAccount_AnswersWithTheFooterAsSaved()
    {
        // Arrange
        var footers = this.Footers();
        var request = new MailFooterRequest("<p>Anna</p><img src=\"cid:logo.png\">", [new MailFooterImageRequest("logo.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])]);

        // Act
        var result = await ClientMailFooterEndpoints.SaveOwnAsync("primary", request, footers, TestContext.Current.CancellationToken);
        var read = await ClientMailFooterEndpoints.ReadOwnAsync("primary", footers, TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.IsType<Ok<MailFooterResponse>>(result.Result).Value!;
        Assert.Equal("<p>Anna</p><img src=\"cid:logo.png\">", saved.Html);
        Assert.Equal("logo.png", Assert.Single(saved.Images).Name);
        var own = Assert.IsType<Ok<ClientOwnMailFooterResponse>>(read.Result).Value!;
        Assert.Equal(saved.Html, own.Footer?.Html);
        Assert.False(own.SetByOrganization);
    }

    /// <summary>An account whose organization sets the footer refuses the member's own and says so.</summary>
    [Fact]
    public async Task SaveOwnAsync_AnAccountWhoseOrganizationSetsTheFooter_AnswersConflictSayingSo()
    {
        // Arrange
        await this.store.SaveTemplateAsync(Organization, new MailFooterContent("<p>{{displayName}}</p>", []), TestContext.Current.CancellationToken);

        // Act
        var result = await ClientMailFooterEndpoints.SaveOwnAsync(
            "held",
            new MailFooterRequest("<p>Anna</p>"),
            this.Footers(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
        Assert.Contains("organization sets the footer", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
    }

    /// <summary>A footer over its bound is refused with a message saying which bound.</summary>
    [Fact]
    public async Task SaveOwnAsync_AFooterOverItsBound_AnswersBadRequestNamingTheBound()
    {
        // Act
        var result = await ClientMailFooterEndpoints.SaveOwnAsync(
            "primary",
            new MailFooterRequest(new string('a', MailFooterContent.MaximumHtmlLength + 1)),
            this.Footers(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains($"{MailFooterContent.MaximumHtmlLength}", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
    }

    /// <summary>A body stating no markup is refused before anything is read.</summary>
    [Fact]
    public async Task SaveOwnAsync_ABodyStatingNoMarkup_AnswersBadRequest()
    {
        // Act
        var result = await ClientMailFooterEndpoints.SaveOwnAsync(
            "primary",
            new MailFooterRequest(),
            this.Footers(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>An account that is not the person's, a blank segment among them, is answered as absent on every verb.</summary>
    /// <param name="accountId">The account segment the request names.</param>
    [Theory]
    [InlineData("elsewhere")]
    [InlineData(" ")]
    public async Task Handlers_AnAccountNotTheirs_AnswerNotFound(string accountId)
    {
        // Arrange
        var footers = this.Footers();

        // Act
        var read = await ClientMailFooterEndpoints.ReadOwnAsync(accountId, footers, TestContext.Current.CancellationToken);
        var save = await ClientMailFooterEndpoints.SaveOwnAsync(accountId, new MailFooterRequest("<p>Anna</p>"), footers, TestContext.Current.CancellationToken);
        var remove = await ClientMailFooterEndpoints.RemoveOwnAsync(accountId, footers, TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(read.Result);
        Assert.IsType<NotFound<ProblemDetails>>(save.Result);
        Assert.IsType<NotFound<ProblemDetails>>(remove.Result);
    }

    /// <summary>Each account sending with a footer is read with whose footer it is; one sending without is absent.</summary>
    [Fact]
    public async Task ReadInForceAsync_OwnAndOrganizationFooters_SaysWhoseEachIs()
    {
        // Arrange
        await this.store.SaveTemplateAsync(Organization, new MailFooterContent("<p>{{displayName}}</p>", []), TestContext.Current.CancellationToken);
        await this.store.SaveOwnAsync(SyntheticUser.Deployment, Primary, new MailFooterContent("<p>Anna</p>", []), TestContext.Current.CancellationToken);

        // Act
        var result = await ClientMailFooterEndpoints.ReadInForceAsync(this.Footers(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("primary", "own"), ("held", "organization")],
            result.Value!.Footers.Select(static footer => (footer.Account, footer.Source)));
    }

    /// <summary>Removing a footer answers without content, whether or not there was one.</summary>
    [Fact]
    public async Task RemoveOwnAsync_TheirOwnAccount_AnswersNoContent()
    {
        // Act
        var result = await ClientMailFooterEndpoints.RemoveOwnAsync("primary", this.Footers(), TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
    }

    /// <summary>Every refusal reason has words, so none reaches a person as an exception.</summary>
    [Fact]
    public void Describe_EveryRefusalReason_SaysSomething()
    {
        // Act
        var descriptions = Enum.GetValues<MailFooterRefusalReason>()
            .Select(static reason => MailFooterRefusals.Describe(new MailFooterRefusal(reason, "logo.png")));

        // Assert
        Assert.All(descriptions, static description => Assert.False(string.IsNullOrWhiteSpace(description)));
    }

    private OwnMailFooters Footers()
    {
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer.RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<EmailContentRenderingBounds>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Unreadable()));

        return new OwnMailFooters(
            AccessAuthorizations.ForUserGranted(SyntheticUser.Deployment, MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite),
            this.store,
            new StubMailAccountAssignments().Assigning(SyntheticUser.Deployment, Primary, Held),
            new MailFootings.VerbatimMailFooterMarkup(),
            MailFootings.Over(this.store),
            new MailFootings.TextAppendingMailFooterComposer(),
            renderer);
    }
}
