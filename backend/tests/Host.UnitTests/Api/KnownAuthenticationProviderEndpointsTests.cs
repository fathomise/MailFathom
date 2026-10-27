// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.KnownAuthenticationProviders;
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
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminConfigurationWrite),
            providers,
            new FakeTimeProvider(Now));
}
