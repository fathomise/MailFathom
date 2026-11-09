// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what reading the recorded verdicts asks for and reports, and the operation no spam command has.</summary>
/// <remarks>
/// What never reaches the terminal is the subject of most of it. A classification prints its signals by name and never by
/// value, so a sending domain or an authentication result cannot arrive in a shell's scrollback through a command about spam.
/// </remarks>
public sealed class SpamCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;
    private const string Account = "work";

    private const string EmptyPage = """{"classifications":[],"nextCursor":null}""";

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The signals print by name, which is how they are recorded and the whole reason the record is safe to read, and the spam subcategory beside them names who named it.</summary>
    [Fact]
    public async Task Classifications_ARecordedVerdict_PrintsItsSignalNamesAndWhatItAskedFor()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(classifications: (HttpStatusCode.OK, """
            {
              "classifications": [
                {
                  "email": "0199a0c0-0000-7000-8000-0000000090a0",
                  "folder": "INBOX",
                  "verdict": "Spam",
                  "decidedBy": "Deterministic",
                  "spamSubcategory": "Phishing",
                  "spamSubcategoryNamedBy": "Model",
                  "phishingRequest": "Credentials",
                  "impersonatedParty": "Bank",
                  "score": 15.2,
                  "threshold": 5.0,
                  "profile": "a1b2c3d4e5f6",
                  "signals": ["X-Spam-Flag", "X-Spam-Status"],
                  "evaluatedAt": "2026-08-12T11:00:00+00:00",
                  "requestedMutations": [
                    {"record": "0199a0c0-0000-7000-8000-0000000090b0", "mutation": "relocate"}
                  ]
                }
              ],
              "nextCursor": null
            }
            """));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Spam (Deterministic 15.2/5)", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("Phishing (named by Model): asks for Credentials, impersonates Bank", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("X-Spam-Flag, X-Spam-Status", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("relocate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Classifications_TheFiltersAnOperatorNamed_ReachTheDeploymentAsAQuery()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(classifications: (HttpStatusCode.OK, EmptyPage));

        // Act
        await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--verdict",
            "Spam",
            "--page-size",
            "10",
            "--endpoint",
            Endpoint);

        // Assert
        var query = deployment.LastClassificationsQuery();

        Assert.Contains("account=work", query, StringComparison.Ordinal);
        Assert.Contains("verdict=Spam", query, StringComparison.Ordinal);
        Assert.Contains("pageSize=10", query, StringComparison.Ordinal);
    }

    /// <summary>A page that ends with a cursor says how to ask for the next one rather than leaving it to be guessed.</summary>
    [Fact]
    public async Task Classifications_APageThatContinues_PrintsTheCursorTheNextOneIsAskedWith()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(classifications: (HttpStatusCode.OK, """
            {
              "classifications": [
                {
                  "email": "0199a0c0-0000-7000-8000-0000000090a0",
                  "folder": "INBOX",
                  "verdict": "NotSpam",
                  "decidedBy": "Deterministic",
                  "score": null,
                  "threshold": null,
                  "profile": null,
                  "signals": [],
                  "evaluatedAt": "2026-08-12T11:00:00+00:00",
                  "requestedMutations": []
                }
              ],
              "nextCursor": "MS4xLjEuYWJj"
            }
            """));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("--cursor MS4xLjEuYWJj", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("no profile", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("none", StringComparison.Ordinal));
    }

    /// <summary>Nothing recorded for an account is an answer, and the answer says what would produce one.</summary>
    [Fact]
    public async Task Classifications_ADeploymentThatHasClassifiedNothing_SaysWhatWouldProduceARecord()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(classifications: (HttpStatusCode.OK, EmptyPage));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("classification run --account work", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Classifications_ADeploymentThatRefusedTheFilters_ReportsAFailure()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(classifications: (
            HttpStatusCode.BadRequest,
            """{"title":"Bad Request","detail":"A page of classifications holds between 1 and 200 records."}"""));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "spam",
            "classifications",
            "--account",
            Account,
            "--page-size",
            "5000",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
    }

    /// <summary>
    /// The absent operations, asserted rather than assumed. Whether mail is classified, which folders it is read in, and
    /// what happens to junk are configuration, so a command that wrote one would be the path around a reviewable diff.
    /// </summary>
    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("threshold")]
    [InlineData("set")]
    public async Task Spam_ACommandThatWouldWriteASetting_DoesNotExist(string verb)
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(classifications: (HttpStatusCode.OK, EmptyPage));

        // Act
        var exitCode = await this.RunAsync(deployment, "spam", verb, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);

    public void Dispose() => this.harness.Dispose();
}
