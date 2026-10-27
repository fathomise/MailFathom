// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.KnownAuthenticationProviders;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the known-provider routes answer, and that a value no match could be made against never reaches the list.</summary>
public sealed class KnownAuthenticationProviderEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly ListedKnownAuthenticationProvider Google = new(
        new Guid("019a2731-0000-7000-8000-0000000000aa"),
        "imap.gmail.com",
        "mx.google.com",
        "user 1 credential 2",
        Now);

    [Fact]
    public async Task AddAsync_AnEntryWrittenInAnyCase_AddsItInTheFormItIsMatchedBy()
    {
        // Arrange
        var providers = Substitute.For<IKnownAuthenticationProviderStore>();
        providers.AddAsync(Arg.Any<KnownAuthenticationProvider>(), Arg.Any<string>(), Now, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await KnownAuthenticationProviderEndpoints.AddAsync(
            new KnownAuthenticationProviderRequest("IMAP.Gmail.com.", "MX.Google.com"),
            Administration(providers),
            TestContext.Current.CancellationToken);

        // Assert
        var added = Assert.IsType<Ok<KnownAuthenticationProviderAddedResponse>>(result.Result).Value!;
        Assert.Equal(("imap.gmail.com", "mx.google.com"), (added.ImapHost, added.AuthorityIdentifier));
    }

    /// <summary>A port, a wildcard, or an authserv-id the account setting would refuse is refused here, without echoing it.</summary>
    /// <param name="imapHost">The host sent.</param>
    /// <param name="authorityIdentifier">The authserv-id sent.</param>
    [Theory]
    [InlineData("imap.gmail.com:993", "mx.google.com")]
    [InlineData("*.gmail.com", "mx.google.com")]
    [InlineData(null, "mx.google.com")]
    [InlineData("imap.gmail.com", "mx google com")]
    [InlineData("imap.gmail.com", null)]
    public async Task AddAsync_AValueNoMatchCouldBeMadeAgainst_IsRefusedWithoutTouchingTheList(
        string? imapHost,
        string? authorityIdentifier)
    {
        // Arrange
        var providers = Substitute.For<IKnownAuthenticationProviderStore>();

        // Act
        var result = await KnownAuthenticationProviderEndpoints.AddAsync(
            new KnownAuthenticationProviderRequest(imapHost, authorityIdentifier),
            Administration(providers),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("gmail", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Empty(providers.ReceivedCalls());
    }

    [Fact]
    public async Task AddAsync_AHostTheListAlreadyNames_IsAConflict()
    {
        // Arrange
        var providers = Substitute.For<IKnownAuthenticationProviderStore>();
        providers.AddAsync(Arg.Any<KnownAuthenticationProvider>(), Arg.Any<string>(), Now, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await KnownAuthenticationProviderEndpoints.AddAsync(
            new KnownAuthenticationProviderRequest("imap.gmail.com", "mx.google.com"),
            Administration(providers),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task RemoveAsync_AHostWrittenInAnyCase_RemovesItsEntry()
    {
        // Arrange
        var providers = Substitute.For<IKnownAuthenticationProviderStore>();
        providers.RemoveAsync("imap.gmail.com", Arg.Any<string>(), Now, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await KnownAuthenticationProviderEndpoints.RemoveAsync(
            "IMAP.gmail.com",
            Administration(providers),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
    }

    /// <summary>A port, a wildcard, or a path names no host the list could hold, so it is refused before the list is touched.</summary>
    /// <param name="imapHost">The host sent.</param>
    [Theory]
    [InlineData("imap.gmail.com:993")]
    [InlineData("*.gmail.com")]
    [InlineData("imap.gmail.com/inbox")]
    public async Task RemoveAsync_AHostNoMatchCouldBeMadeAgainst_IsRefusedWithoutTouchingTheList(string imapHost)
    {
        // Arrange
        var providers = Substitute.For<IKnownAuthenticationProviderStore>();

        // Act
        var result = await KnownAuthenticationProviderEndpoints.RemoveAsync(
            imapHost,
            Administration(providers),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("gmail", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Empty(providers.ReceivedCalls());
    }

    /// <summary>A first page with more following answers every entry and the cursor the next page is asked with.</summary>
    [Fact]
    public async Task ListAsync_AFirstPageWithMoreFollowing_AnswersItsEntriesAndTheCursorTheNextPageIsAskedWith()
    {
        // Arrange
        var providers = Substitute.For<IKnownAuthenticationProviderStore>();
        providers.ReadAsync(Arg.Is<AdministrativeListingQuery>(query => query!.After == null), Arg.Any<CancellationToken>())
            .Returns(new KnownAuthenticationProviderListing([Google], ContinuesAfter: Google.Id));

        // Act
        var result = await KnownAuthenticationProviderEndpoints.ListAsync(
            pageSize: null,
            cursor: null,
            Administration(providers),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<KnownAuthenticationProviderListResponse>>(result.Result).Value!;
        Assert.Equal(
            new KnownAuthenticationProviderResponse("imap.gmail.com", "mx.google.com", "user 1 credential 2", Now),
            Assert.Single(page.Providers));
        Assert.True(AdministrativeListingCursor.TryDecode(page.NextCursor, AdministrativeListing.KnownAuthenticationProviders, out var after));
        Assert.Equal(Google.Id, after);
    }

    /// <summary>The cursor a page answered with is where the following page continues, and the last page answers none.</summary>
    [Fact]
    public async Task ListAsync_TheCursorOfThePreviousPage_ReadsTheLastPageAfterIt()
    {
        // Arrange
        var providers = Substitute.For<IKnownAuthenticationProviderStore>();
        providers.ReadAsync(Arg.Is<AdministrativeListingQuery>(query => query!.After == Google.Id), Arg.Any<CancellationToken>())
            .Returns(new KnownAuthenticationProviderListing([Google], ContinuesAfter: null));

        // Act
        var result = await KnownAuthenticationProviderEndpoints.ListAsync(
            pageSize: null,
            AdministrativeListingCursor.Encode(AdministrativeListing.KnownAuthenticationProviders, Google.Id),
            Administration(providers),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<KnownAuthenticationProviderListResponse>>(result.Result).Value!;
        Assert.Single(page.Providers);
        Assert.Null(page.NextCursor);
    }

    /// <summary>A cursor another listing issued names no position in this one, so it is refused before anything is read.</summary>
    [Fact]
    public async Task ListAsync_ACursorAnotherListingIssued_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var providers = Substitute.For<IKnownAuthenticationProviderStore>();

        // Act
        var result = await KnownAuthenticationProviderEndpoints.ListAsync(
            pageSize: null,
            AdministrativeListingCursor.Encode(AdministrativeListing.Organizations, Google.Id),
            Administration(providers),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Empty(providers.ReceivedCalls());
    }

    [Fact]
    public async Task RemoveAsync_AHostTheListDoesNotName_IsNotFound()
    {
        // Arrange
        var providers = Substitute.For<IKnownAuthenticationProviderStore>();

        // Act
        var result = await KnownAuthenticationProviderEndpoints.RemoveAsync(
            "imap.example.test",
            Administration(providers),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    private static KnownAuthenticationProviderAdministration Administration(IKnownAuthenticationProviderStore providers) =>
        new(
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminRead, MailFathomPermission.AdminConfigurationWrite),
            providers,
            new FakeTimeProvider(Now));
}
