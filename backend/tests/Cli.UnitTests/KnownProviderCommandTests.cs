// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Commands.KnownProviders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the commands that list, add, and remove the known authentication providers.</summary>
/// <remarks>
/// What these hold is the part of each act that lives in the command: which route it reaches, that an addition states
/// what an entry rests on and is never sent without somebody agreeing to it, and that a refusal reaches the operator.
/// </remarks>
public sealed class KnownProviderCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task List_TheDeploymentsEntries_DrawsEachHostBesideItsAuthservId()
    {
        // Arrange
        using var deployment = Answering(
            HttpMethod.Get,
            AdminEndpointRoutes.KnownProvidersPath,
            HttpStatusCode.OK,
            """
            {"providers":[{"imapHost":"imap.gmail.com","authorityIdentifier":"mx.google.com","addedBy":"migration","addedAt":"2026-10-27T09:00:00+00:00"}],"nextCursor":null}
            """);

        // Act
        var exitCode = await this.harness.RunAsync(deployment, "known-provider", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var listing = DrawnListing.ReadFrom(this.harness.Console.Lines, "IMAP host", "Authserv-id", "Added by", "Added");

        Assert.Equal(
            [("imap.gmail.com", "mx.google.com")],
            listing.Rows.Select(row => (listing.Cell(row, "IMAP host"), listing.Cell(row, "Authserv-id"))));
    }

    [Fact]
    public async Task Add_Confirmed_StatesTheRequirementsAndPostsTheEntry()
    {
        // Arrange
        using var deployment = Answering(
            HttpMethod.Post,
            AdminEndpointRoutes.KnownProvidersPath,
            HttpStatusCode.OK,
            """{"imapHost":"imap.example.test","authorityIdentifier":"mx.example.test"}""");

        // Act
        var exitCode = await this.harness.RunAsync(
            deployment,
            "known-provider",
            "add",
            "--host",
            "IMAP.Example.Test",
            "--authserv-id",
            "mx.example.test",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(AddKnownProviderCommand.Requirements, this.harness.Console.Warnings);
        Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.KnownProvidersPath));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("imap.example.test", StringComparison.Ordinal) && line.Contains("mx.example.test", StringComparison.Ordinal));
    }

    /// <summary>An entry decides what every account on a host believes, so an exhausted pipe never agrees for somebody.</summary>
    [Fact]
    public async Task Add_NobodyAtTheTerminalAndNoYes_SendsNothing()
    {
        // Arrange
        this.harness.Console.AnswersQuestions = false;
        using var deployment = Answering(HttpMethod.Post, AdminEndpointRoutes.KnownProvidersPath, HttpStatusCode.OK, "{}");

        // Act
        var exitCode = await this.harness.RunAsync(
            deployment,
            "known-provider",
            "add",
            "--host",
            "imap.example.test",
            "--authserv-id",
            "mx.example.test",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.KnownProvidersPath));
    }

    /// <summary>A host the deployment cannot match is refused there, and the refusal is what the operator reads.</summary>
    [Fact]
    public async Task Add_HostTheDeploymentRefuses_ReportsItsRefusal()
    {
        // Arrange
        using var deployment = Answering(
            HttpMethod.Post,
            AdminEndpointRoutes.KnownProvidersPath,
            HttpStatusCode.BadRequest,
            """{"detail":"The IMAP host must be a host name or an address, with no port, wildcard, path, or whitespace."}""");

        // Act
        var exitCode = await this.harness.RunAsync(
            deployment,
            "known-provider",
            "add",
            "--host",
            "*.example.test",
            "--authserv-id",
            "mx.example.test",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("no port, wildcard", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remove_AHost_DeletesItsEntry()
    {
        // Arrange
        using var deployment = Answering(
            HttpMethod.Delete,
            AdminEndpointRoutes.KnownProviderPath("imap.example.test"),
            HttpStatusCode.NoContent,
            string.Empty);

        // Act
        var exitCode = await this.harness.RunAsync(
            deployment, "known-provider", "remove", "--host", "imap.example.test", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Delete, AdminEndpointRoutes.KnownProviderPath("imap.example.test")));
    }

    public void Dispose() => this.harness.Dispose();

    private static FakeHttpMessageHandler Answering(HttpMethod method, string path, HttpStatusCode status, string body) =>
        new((request, _) => Task.FromResult(
            request.Method == method && request.RequestUri?.AbsolutePath == path
                ? FakeAdminEndpoint.Json(status, body)
                : FakeAdminEndpoint.AnswerSession(request) ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty)));
}
