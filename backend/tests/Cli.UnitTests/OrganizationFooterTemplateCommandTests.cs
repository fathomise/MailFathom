// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the commands that set, show, and clear an organization's footer template.</summary>
public sealed class OrganizationFooterTemplateCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private static readonly Guid Organization = new("77777777-7777-7777-7777-777777777777");

    private static readonly byte[] Logo = [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));

    private readonly string files = Path.Combine(Path.GetTempPath(), $"mailfathom-footer-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Set_AMarkupFileAndAnImage_PutsBothToTheTemplateRoute()
    {
        // Arrange
        var html = await this.WriteAsync("footer.html", "<p>{{displayName}}</p><img src=\"cid:logo.png\">"u8.ToArray());
        var logo = await this.WriteAsync("logo.png", Logo);
        using var deployment = Answering(
            AdminEndpointRoutes.OrganizationFooterTemplatePath(Organization),
            """{"html":"<p>{{displayName}}</p>","images":[]}""");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "organization",
            "footer-template",
            "set",
            "--organization",
            $"{Organization:D}",
            "--html-file",
            html,
            "--image",
            $"logo.png={logo}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var request = Assert.Single(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.OrganizationFooterTemplatePath(Organization)));
        using var body = JsonDocument.Parse(request.ContentAsUtf8String());
        Assert.Equal("<p>{{displayName}}</p><img src=\"cid:logo.png\">", body.RootElement.GetProperty("html").GetString());
        var image = Assert.Single(body.RootElement.GetProperty("images").EnumerateArray());
        Assert.Equal(("logo.png", "image/png"), (image.GetProperty("name").GetString(), image.GetProperty("mediaType").GetString()));
        Assert.Equal(Logo, image.GetProperty("content").GetBytesFromBase64());
    }

    [Fact]
    public async Task Set_AnImageOfATypeAFooterDoesNotCarry_FailsAndSendsNothing()
    {
        // Arrange
        var html = await this.WriteAsync("footer.html", "<p>{{displayName}}</p>"u8.ToArray());
        var logo = await this.WriteAsync("logo.svg", "<svg/>"u8.ToArray());
        using var deployment = Answering(AdminEndpointRoutes.OrganizationFooterTemplatePath(Organization), "{}");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "organization",
            "footer-template",
            "set",
            "--organization",
            $"{Organization:D}",
            "--html-file",
            html,
            "--image",
            $"logo.svg={logo}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.OrganizationFooterTemplatePath(Organization)));
    }

    [Fact]
    public async Task Show_ASetTemplate_PrintsItsMarkupAndItsImages()
    {
        // Arrange
        using var deployment = Answering(
            AdminEndpointRoutes.OrganizationFooterTemplatePath(Organization),
            $$$"""{"template":{"html":"<p>{{displayName}}</p>","images":[{"name":"logo.png","mediaType":"image/png","content":"{{{Convert.ToBase64String(Logo)}}}"}]}}""");

        // Act
        var exitCode = await this.RunAsync(
            deployment, "organization", "footer-template", "show", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains("<p>{{displayName}}</p>", this.harness.Console.Lines);
        Assert.Contains($"image logo.png: image/png, {Logo.Length} bytes", this.harness.Console.Lines);
    }

    [Fact]
    public async Task Show_NoTemplate_SaysEachMemberSendsWithTheirOwn()
    {
        // Arrange
        using var deployment = Answering(AdminEndpointRoutes.OrganizationFooterTemplatePath(Organization), """{"template":null}""");

        // Act
        var exitCode = await this.RunAsync(
            deployment, "organization", "footer-template", "show", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("sets no footer template", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Clear_AnOrganization_DeletesItsTemplate()
    {
        // Arrange
        using var deployment = Answering(AdminEndpointRoutes.OrganizationFooterTemplatePath(Organization), string.Empty, HttpStatusCode.NoContent);

        // Act
        var exitCode = await this.RunAsync(
            deployment, "organization", "footer-template", "clear", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Delete, AdminEndpointRoutes.OrganizationFooterTemplatePath(Organization)));
    }

    public void Dispose()
    {
        this.harness.Dispose();

        if (Directory.Exists(this.files))
        {
            Directory.Delete(this.files, recursive: true);
        }
    }

    private static FakeHttpMessageHandler Answering(string path, string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new((request, _) => Task.FromResult(
            request.RequestUri?.AbsolutePath == path
                ? FakeAdminEndpoint.Json(status, body)
                : FakeAdminEndpoint.AnswerSession(request) ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty)));

    private async Task<string> WriteAsync(string name, byte[] content)
    {
        var path = Path.Combine(this.files, name);

        Directory.CreateDirectory(this.files);
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);

        return path;
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
