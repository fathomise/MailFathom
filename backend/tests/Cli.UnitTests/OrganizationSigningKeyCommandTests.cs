// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the commands that list, rotate, and revoke an organization's signing keys.</summary>
/// <remarks>
/// What these hold is the part of each act that lives in the command: which route it reaches, what it reports back, and
/// that a revocation, which nothing undoes, is never sent without somebody agreeing to it.
/// </remarks>
public sealed class OrganizationSigningKeyCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private static readonly Guid Organization = new("77777777-7777-7777-7777-777777777777");

    private static readonly Guid ActiveKey = new("0199a0c0-0000-7000-8000-0000000000a1");

    private static readonly Guid RetiredKey = new("0199a0c0-0000-7000-8000-0000000000a0");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task List_AnOrganizationsKeys_DrawsEachKeysStateUnderItsHeading()
    {
        // Arrange
        using var deployment = Answering(
            AdminEndpointRoutes.OrganizationSigningKeysPath(Organization),
            $$"""
              {"organizationId":"{{Organization:D}}","displayName":"Acme Corporation","keys":[
                {"keyId":"{{ActiveKey:D}}","state":"active","createdAt":"2026-10-26T09:00:00+00:00","retiredAt":null,"revokedAt":null},
                {"keyId":"{{RetiredKey:D}}","state":"retired","createdAt":"2025-10-26T09:00:00+00:00","retiredAt":"2026-10-26T09:00:00+00:00","revokedAt":null}]}
              """);

        // Act
        var exitCode = await this.RunAsync(
            deployment, "organization", "signing-key", "list", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var listing = DrawnListing.ReadFrom(this.harness.Console.Lines, "Key", "State", "Created", "Retired", "Revoked");

        Assert.Equal(
            [($"{ActiveKey:D}", "active", "-"), ($"{RetiredKey:D}", "retired", "2026-10-26 09:00:00Z")],
            listing.Rows.Select(row => (listing.Cell(row, "Key"), listing.Cell(row, "State"), listing.Cell(row, "Retired"))));
    }

    [Fact]
    public async Task Rotate_AnOrganization_PostsToItsRotationRouteAndNamesBothKeys()
    {
        // Arrange
        using var deployment = Answering(
            AdminEndpointRoutes.OrganizationSigningKeyRotationPath(Organization),
            $$"""{"activeKeyId":"{{ActiveKey:D}}","retiredKeyId":"{{RetiredKey:D}}"}""");

        // Act
        var exitCode = await this.RunAsync(
            deployment, "organization", "signing-key", "rotate", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.OrganizationSigningKeyRotationPath(Organization)));

        var report = string.Join('\n', this.harness.Console.Lines);
        Assert.Contains($"{ActiveKey:D}", report, StringComparison.Ordinal);
        Assert.Contains($"{RetiredKey:D}", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_ConfirmedForTheActiveKey_PostsToItsRevocationRouteAndNamesTheReplacement()
    {
        // Arrange
        using var deployment = Answering(
            AdminEndpointRoutes.OrganizationSigningKeyRevocationPath(Organization, RetiredKey),
            $$"""{"revokedKeyId":"{{RetiredKey:D}}","wasActive":true,"activeKeyId":"{{ActiveKey:D}}"}""");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "organization",
            "signing-key",
            "revoke",
            "--organization",
            $"{Organization:D}",
            "--key",
            $"{RetiredKey:D}",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(
            HttpMethod.Post,
            AdminEndpointRoutes.OrganizationSigningKeyRevocationPath(Organization, RetiredKey)));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains($"now signs with key {ActiveKey:D}", StringComparison.Ordinal));
    }

    /// <summary>A revoked key stays revoked, so an exhausted pipe never answers the question for somebody.</summary>
    [Fact]
    public async Task Revoke_NobodyAtTheTerminalAndNoYes_SendsNothing()
    {
        // Arrange
        this.harness.Console.AnswersQuestions = false;
        using var deployment = Answering(AdminEndpointRoutes.OrganizationSigningKeyRevocationPath(Organization, ActiveKey), "{}");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "organization",
            "signing-key",
            "revoke",
            "--organization",
            $"{Organization:D}",
            "--key",
            $"{ActiveKey:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(
            HttpMethod.Post,
            AdminEndpointRoutes.OrganizationSigningKeyRevocationPath(Organization, ActiveKey)));
    }

    public void Dispose() => this.harness.Dispose();

    private static FakeHttpMessageHandler Answering(string path, string body) =>
        new((request, _) => Task.FromResult(
            request.RequestUri?.AbsolutePath == path
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, body)
                : FakeAdminEndpoint.AnswerSession(request) ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty)));

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
