// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.AppHost.UnitTests;

public sealed class DevelopmentCredentialProvisionerTests
{
    private static readonly Uri StartedEndpoint = new("http://127.0.0.1:5100/started");
    private static readonly Uri AdminEndpoint = new("http://127.0.0.1:5200/");
    private static readonly Guid UserId = Guid.Parse("b107de3d-4331-4755-8b17-3270dbe53b59");

    [Fact]
    public async Task WaitForSoleServedUserAsync_AHostStillStarting_AsksAgainAndThenNamesTheOneUserItHolds()
    {
        // Arrange
        using var responses = new RecordingHandler(
            Response(HttpStatusCode.ServiceUnavailable),
            Response(HttpStatusCode.OK),
            JsonResponse($$"""{"users":[{"id":"{{UserId}}","served":true,"mcpEndpoint":true,"clientEndpoint":true}]}"""));
        using var client = new HttpClient(responses);
        var timeProvider = new FakeTimeProvider();
        var provisioner = new DevelopmentCredentialProvisioner(client, timeProvider);

        // Act
        var reading = provisioner.WaitForSoleServedUserAsync(
            StartedEndpoint,
            AdminEndpoint,
            TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var user = await reading;

        // Assert
        Assert.Equal(UserId, user);
        Assert.Equal(
            [
                $"GET {StartedEndpoint}",
                $"GET {StartedEndpoint}",
                "GET http://127.0.0.1:5200/api/admin/users",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));
    }

    [Fact]
    public async Task WaitForSoleServedUserAsync_AHostHoldingNobody_RecordsTheUserAndNamesThem()
    {
        // Arrange
        using var responses = new RecordingHandler(
            Response(HttpStatusCode.OK),
            JsonResponse("""{"users":[]}"""),
            JsonResponse($$"""{"id":"{{UserId}}"}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, new FakeTimeProvider());

        // Act
        var user = await provisioner.WaitForSoleServedUserAsync(
            StartedEndpoint,
            AdminEndpoint,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserId, user);
        Assert.Equal(
            [
                $"GET {StartedEndpoint}",
                "GET http://127.0.0.1:5200/api/admin/users",
                "POST http://127.0.0.1:5200/api/admin/users",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));
        using var body = JsonDocument.Parse(responses.Requests[^1].Body!);
        Assert.Equal("user", body.RootElement.GetProperty("displayName").GetString());
    }

    /// <summary>Every first start records the default administrator with both mail switches off, so the roster carries it beside the user the launch serves, or alone.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WaitForSoleServedUserAsync_ARosterCarryingTheDefaultAdministrator_PassesOverIt(bool aServedUserIsRecorded)
    {
        // Arrange
        var administrator = """{"id":"0f0e0d0c-0b0a-4908-8706-050403020100","served":true,"mcpEndpoint":false,"clientEndpoint":false}""";
        var served = $$"""{"id":"{{UserId}}","served":true,"mcpEndpoint":true,"clientEndpoint":true}""";
        using var responses = aServedUserIsRecorded
            ? new RecordingHandler(
                Response(HttpStatusCode.OK),
                JsonResponse($$"""{"users":[{{administrator}},{{served}}]}"""))
            : new RecordingHandler(
                Response(HttpStatusCode.OK),
                JsonResponse($$"""{"users":[{{administrator}}]}"""),
                JsonResponse($$"""{"id":"{{UserId}}"}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, new FakeTimeProvider());

        // Act
        var user = await provisioner.WaitForSoleServedUserAsync(
            StartedEndpoint,
            AdminEndpoint,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserId, user);
        Assert.Equal(aServedUserIsRecorded ? 2 : 3, responses.Requests.Count);
    }

    [Fact]
    public async Task EnsureCredentialAsync_CredentialDoesNotExist_ProvisionsIt()
    {
        // Arrange
        using var responses = new RecordingHandler(
            JsonResponse($$"""{"user":"{{UserId}}","credentials":[]}"""),
            JsonResponse("{}"));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var credentialCreated = await provisioner.EnsureCredentialAsync(
            AdminEndpoint,
            UserId,
            "test",
            "test-password",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(credentialCreated);
        Assert.Equal(
            [
                $"GET http://127.0.0.1:5200/api/admin/users/{UserId:D}/credentials",
                $"POST http://127.0.0.1:5200/api/admin/users/{UserId:D}/credentials",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));
        Assert.Equal(
            "{\"method\":\"password\",\"username\":\"test\",\"password\":\"test-password\",\"permissions\":null}",
            responses.Requests[^1].Body);
    }

    [Fact]
    public async Task EnsureCredentialAsync_CredentialAlreadyExists_LeavesItUnchanged()
    {
        // Arrange
        using var responses = new RecordingHandler(
            JsonResponse(
                $$"""{"user":"{{UserId}}","credentials":[{"method":"password","lookup":"test","enabled":true}]}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var credentialCreated = await provisioner.EnsureCredentialAsync(
            AdminEndpoint,
            UserId,
            "test",
            "test-password",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(credentialCreated);
        Assert.Single(responses.Requests);
    }

    [Fact]
    public async Task EnsureMailAccountAsync_NoAccountAssignedForTheMailbox_CreatesTheOneTheRunCollected()
    {
        // Arrange
        using var responses = new RecordingHandler(
            AccountsResponse(),
            JsonResponse("""{"committed":true,"version":5,"code":null,"messages":[],"accountId":"5b0c2f7e-8d1a-4c3b-9e6f-1a2b3c4d5e6f"}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var declared = await provisioner.EnsureMailAccountAsync(
            AdminEndpoint,
            UserId,
            "imap.example.test",
            "someone@example.test",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(declared);
        Assert.Equal(
            [
                "GET http://127.0.0.1:5200/api/admin/mail-accounts",
                "POST http://127.0.0.1:5200/api/admin/mail-accounts",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));

        using var body = JsonDocument.Parse(responses.Requests[^1].Body!);
        Assert.Equal(UserId, body.RootElement.GetProperty("userId").GetGuid());

        using var account = JsonDocument.Parse(body.RootElement.GetProperty("account").GetString()!);
        Assert.False(account.RootElement.TryGetProperty("AccountId", out _));
        Assert.Equal("someone@example.test", account.RootElement.GetProperty("EmailAddress").GetString());
        Assert.Equal(
            OrchestrationContract.DevelopmentMailAccountDisplayName,
            account.RootElement.GetProperty("DisplayName").GetString());
        Assert.Equal("imap.example.test", account.RootElement.GetProperty("Host").GetString());
        Assert.Equal("someone@example.test", account.RootElement.GetProperty("UserName").GetString());

        var password = account.RootElement.GetProperty("Secrets").GetProperty("Password");
        Assert.Equal(
            OrchestrationContract.DevelopmentMailAccountPasswordName,
            password.GetProperty("Name").GetString());
        Assert.Equal(
            $"env:{OrchestrationContract.DevelopmentMailAccountPasswordVariable}",
            password.GetProperty("SecretReference").GetString());
        Assert.Equal("English", account.RootElement.GetProperty("Language").GetString());
    }

    [Fact]
    public async Task EnsureMailAccountAsync_BareLogin_ComposesTheAddressFromTheLoginAndTheServer()
    {
        // Arrange
        using var responses = new RecordingHandler(
            AccountsResponse(),
            JsonResponse("""{"committed":true,"version":1,"code":null,"messages":[],"accountId":null}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        await provisioner.EnsureMailAccountAsync(
            AdminEndpoint,
            UserId,
            "localhost",
            "someone",
            TestContext.Current.CancellationToken);

        // Assert
        using var body = JsonDocument.Parse(responses.Requests[^1].Body!);
        using var account = JsonDocument.Parse(body.RootElement.GetProperty("account").GetString()!);
        Assert.Equal("someone@localhost", account.RootElement.GetProperty("EmailAddress").GetString());
    }

    [Fact]
    public async Task EnsureMailAccountAsync_AnAccountForThatAddressAlreadyAssigned_LeavesItUnchanged()
    {
        // Arrange
        using var responses = new RecordingHandler(
            AccountsResponse((UserId, "SOMEONE@example.test")));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var declared = await provisioner.EnsureMailAccountAsync(
            AdminEndpoint,
            UserId,
            "imap.example.test",
            "someone@example.test",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(declared);
        Assert.Single(responses.Requests);
    }

    [Fact]
    public async Task EnsureMailAccountAsync_ADeploymentRefusingTheDeclaration_ReportsWhatItRefused()
    {
        // Arrange
        using var responses = new RecordingHandler(
            AccountsResponse((Guid.NewGuid(), "someone@example.test")),
            JsonResponse(
                """{"committed":false,"version":0,"code":12040,"messages":["The mailbox names no host."],"accountId":null}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.EnsureMailAccountAsync(
            AdminEndpoint,
            UserId,
            "imap.example.test",
            "someone@example.test",
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("The mailbox names no host.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureUserRoleAsync_TheUserHoldsNoRole_AssignsTheSeededUserRoleAtTheirOwnScope()
    {
        // Arrange
        var userRole = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-901a2b3c4d5e");
        using var responses = new RecordingHandler(
            RolesResponse((Guid.Parse("11111111-2222-4333-8444-555555555555"), "Administrator"), (userRole, "User")),
            JsonResponse("""{"id":"0c9b8a7d-6e5f-4a3b-9c2d-1e0f9a8b7c6d"}"""));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var assigned = await provisioner.EnsureUserRoleAsync(
            AdminEndpoint,
            UserId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(assigned);
        Assert.Equal(
            [
                "GET http://127.0.0.1:5200/api/admin/roles",
                "POST http://127.0.0.1:5200/api/admin/role-assignments",
            ],
            responses.Requests.Select(static request => $"{request.Method} {request.Address}"));
        Assert.Equal(
            $$"""{"roleId":"{{userRole}}","principalKind":"user","principalId":"{{UserId}}","scopeKind":"user","scopeId":"{{UserId}}"}""",
            responses.Requests[^1].Body);
    }

    [Fact]
    public async Task EnsureUserRoleAsync_TheRoleIsAlreadyAssigned_LeavesItUnchanged()
    {
        // Arrange
        using var responses = new RecordingHandler(
            RolesResponse((Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-901a2b3c4d5e"), "User")),
            Response(HttpStatusCode.Conflict));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var assigned = await provisioner.EnsureUserRoleAsync(
            AdminEndpoint,
            UserId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(assigned);
        Assert.Equal(2, responses.Requests.Count);
    }

    [Fact]
    public async Task EnsureUserRoleAsync_ADeploymentHoldingNoUserRole_ReportsIt()
    {
        // Arrange
        using var responses = new RecordingHandler(
            RolesResponse((Guid.Parse("11111111-2222-4333-8444-555555555555"), "Administrator")));
        using var client = new HttpClient(responses);
        var provisioner = new DevelopmentCredentialProvisioner(client, TimeProvider.System);

        // Act
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.EnsureUserRoleAsync(
            AdminEndpoint,
            UserId,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("'User'", refused.Message, StringComparison.Ordinal);
        Assert.Single(responses.Requests);
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode) => new(statusCode);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    /// <summary>Answers the account listing, which names each account by the address and the display name its record holds.</summary>
    private static HttpResponseMessage AccountsResponse(params (Guid User, string EmailAddress)[] accounts) => JsonResponse(
        $$"""
        {"accounts":[{{string.Join(",", accounts.Select(static account =>
            $$"""{"id":"{{Guid.NewGuid()}}","version":1,"users":["{{account.User}}"],"emailAddress":{{JsonSerializer.Serialize(account.EmailAddress)}},"displayName":"Local mailbox"}"""))}}],"nextCursor":null}
        """);

    /// <summary>Answers the role listing, which names each role by the identifier its assignments carry and the name an operator reads.</summary>
    private static HttpResponseMessage RolesResponse(params (Guid Id, string Name)[] roles) => JsonResponse(
        $$"""
        {"roles":[{{string.Join(",", roles.Select(static role =>
            $$"""{"id":"{{role.Id}}","name":"{{role.Name}}","permissions":[],"patterns":[],"unpublished":[],"createdAt":"2026-11-08T00:00:00+00:00"}"""))}}],"nextCursor":null}
        """);

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        internal List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return this.responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Address, string? Body);
}
