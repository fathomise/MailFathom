// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Net;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.OAuth;

/// <summary>Covers how many times one token request reaches an authorization server that is refusing.</summary>
/// <remarks>
/// <para>
/// The answer has to be once, and nothing about the registration says so on its own. The host's service defaults add the
/// standard resilience handler to every client the factory builds, <see cref="MailOAuthAccessTokenSource" /> already runs
/// the exchange under the <c>MailAuthorizationServerInvocation</c> pipeline, and three attempts wrapped by three is nine
/// token requests into a server that is already struggling. Infrastructure removes the handler for this one client to
/// prevent that.
/// </para>
/// <para>
/// The removal is order-dependent, which is the whole reason this is asserted rather than read. It takes out what was
/// registered before it, so it works only while the composition root adds the service defaults ahead of the
/// infrastructure. Nothing in either file fails if that order is swapped; this does.
/// </para>
/// </remarks>
public sealed class MailOAuthTokenTransportTests
{
    private static readonly Uri TokenEndpoint = new("https://sso.example.test/oauth2/token");

    private const int SizeLimit = ServiceCollectionExtensions.MailOAuthTokenResponseSizeLimitInBytes;

    /// <summary>The transport carries no retry of its own, so a refusal is one request rather than a burst of them.</summary>
    [Fact]
    public async Task MailOAuthTransport_ComposedUnderTheServiceDefaults_ReachesARefusingServerOnce()
    {
        // Arrange
        using var authorizationServer = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await using var provider = ComposedServices(authorizationServer);
        using var transport = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(MailOAuthAccessTokenSource.TransportName);

        // Act
        using var response = await transport.GetAsync(TokenEndpoint, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(authorizationServer.RecordedRequests);
    }

    /// <summary>The defaults the test composes are live, so the assertion above is a removal rather than a handler that was never added.</summary>
    [Fact]
    public async Task AnOrdinaryClient_ComposedUnderTheSameDefaults_RetriesARefusal()
    {
        // Arrange
        using var refusingServer = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var services = ComposedServiceCollection();
        services.AddHttpClient("ordinary")
            .ConfigurePrimaryHttpMessageHandler(() => refusingServer);

        await using var provider = services.BuildServiceProvider();
        using var transport = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ordinary");

        // Act
        using var response = await transport.GetAsync(TokenEndpoint, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(refusingServer.RecordedRequests.Count > 1);
    }

    /// <summary>
    /// A token response is a few hundred bytes, so the transport reads no more than a bound far above that. An answer
    /// declaring more is refused before its body is read.
    /// </summary>
    [Fact]
    public async Task MailOAuthTransport_AnAnswerDeclaringMoreThanTheLimit_IsRefusedNamingTheLimit()
    {
        // Arrange
        using var authorizationServer = AuthorizationServerAnswering(() => new ByteArrayContent(new byte[SizeLimit + 1]));

        await using var provider = ComposedServices(authorizationServer);
        using var transport = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(MailOAuthAccessTokenSource.TransportName);

        // Act
        var refusal = await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.GetAsync(TokenEndpoint, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(SizeLimit.ToString(CultureInfo.InvariantCulture), refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An endpoint that keeps sending without saying how much is the case the bound exists for: nothing can be refused
    /// up front, so the body is buffered against the limit and the read ends there.
    /// </summary>
    [Fact]
    public async Task MailOAuthTransport_AnAnswerOfUndeclaredLengthPastTheLimit_IsRefusedNamingTheLimit()
    {
        // Arrange
        using var authorizationServer = AuthorizationServerAnswering(() => new UndeclaredLengthContent(SizeLimit + 1));

        await using var provider = ComposedServices(authorizationServer);
        using var transport = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(MailOAuthAccessTokenSource.TransportName);

        // Act
        var refusal = await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.GetAsync(TokenEndpoint, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(SizeLimit.ToString(CultureInfo.InvariantCulture), refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The refusals above are the bound's, so an answer of exactly the limit still arrives.</summary>
    [Fact]
    public async Task MailOAuthTransport_AnAnswerOfExactlyTheLimit_IsRead()
    {
        // Arrange
        using var authorizationServer = AuthorizationServerAnswering(() => new UndeclaredLengthContent(SizeLimit));

        await using var provider = ComposedServices(authorizationServer);
        using var transport = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(MailOAuthAccessTokenSource.TransportName);

        // Act
        using var response = await transport.GetAsync(TokenEndpoint, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SizeLimit, body.Length);
    }

    private static FakeHttpMessageHandler AuthorizationServerAnswering(Func<HttpContent> createBody) =>
        FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = createBody() });

    private static ServiceProvider ComposedServices(FakeHttpMessageHandler authorizationServer)
    {
        var services = ComposedServiceCollection();

        // The registration's own primary handler opens a socket, so the test replaces it. Assignment rather than
        // appending is what lets it: the same call in the registration sets the handler that this one then replaces.
        services.AddHttpClient(MailOAuthAccessTokenSource.TransportName)
            .ConfigurePrimaryHttpMessageHandler(() => authorizationServer);

        return services.BuildServiceProvider();
    }

    /// <summary>Composes the two registrations whose order decides the outcome, as the host composes them.</summary>
    /// <remarks>
    /// One deviation from the host, and it is timing rather than structure: the retry delay is removed so that a client
    /// still carrying the handler is observed retrying in milliseconds. Left at the standard backoff, the control below
    /// would spend the better part of a minute proving the same thing, and a suite that must stay fast would have paid
    /// that on every run.
    /// </remarks>
    private static ServiceCollection ComposedServiceCollection()
    {
        var services = new ServiceCollection();

        services.ConfigureHttpClientDefaults(builder => builder
            .AddStandardResilienceHandler()
            .Configure(static options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
            }));
        services.AddInfrastructure(
            static _ => new PostgresConnectionSettings(
                "Host=postgres.example.test;Database=mailfathom",
                ConnectionStringSecret: null,
                Password: null),
            PostgresTextSearchConfiguration.Create("simple"),
            MailAnsweringBudget.Default);

        return services;
    }
}
