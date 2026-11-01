// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Organizations;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Sets the footer template every member's message from an organization's accounts carries.</summary>
/// <remarks>
/// The markup is read from a file rather than an argument, because a footer is several lines of HTML that a shell would
/// mangle, and each image from a file of its own, named as the markup refers to it. Both are read before anything is
/// sent, so a path that is not there is answered without a request.
/// </remarks>
internal static class SetOrganizationFooterTemplateCommand
{
    /// <summary>The image types a footer carries, by the extension a file of each is named with.</summary>
    private static readonly Dictionary<string, string> MediaTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
    };

    /// <summary>Builds the <c>organization footer-template set</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var organizationOption = OrganizationOptions.Organization();

        Option<FileInfo> htmlFileOption = new("--html-file")
        {
            Description = "The file holding the template's HTML. It names a member's details as {{displayName}}, {{jobTitle}}, and {{phoneNumber}}, and each image as cid: followed by its name.",
            Required = true,
        };

        Option<string[]> imageOption = new("--image")
        {
            Description = "An image the template shows, as name=path, where name is what the HTML refers to after cid:. A PNG, JPEG, or GIF file. Repeat for each image.",
            AllowMultipleArgumentsPerToken = false,
        };

        Command command = new("set", "Set the footer every member's message from this organization's accounts carries.")
        {
            organizationOption,
            htmlFileOption,
            imageOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(organizationOption),
            result.GetValue(htmlFileOption)!,
            result.GetValue(imageOption) ?? [],
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid organization,
        FileInfo htmlFile,
        IReadOnlyList<string> images,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var template = new OrganizationFooterTemplate(
            await ReadAsync(htmlFile, File.ReadAllTextAsync, cancellationToken),
            [.. await Task.WhenAll(images.Select(image => ReadImageAsync(image, cancellationToken)))]);

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var saved = await deployment.SetOrganizationFooterTemplateAsync(profile.Token, organization, template, cancellationToken);

        context.Console.WriteLine(
            $"Organization {organization:D} now sends with a footer template carrying {saved.Images.Count} image(s).");

        return CliExitCode.Success;
    }

    private static async Task<OrganizationFooterTemplateImage> ReadImageAsync(string image, CancellationToken cancellationToken)
    {
        var separator = image.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == image.Length - 1)
        {
            throw new CliFailure($"'--image {image}' names no image: write it as name=path.");
        }

        var file = new FileInfo(image[(separator + 1)..]);

        if (!MediaTypesByExtension.TryGetValue(file.Extension, out var mediaType))
        {
            throw new CliFailure($"'{file.Name}' is not a .png, .jpg, .jpeg, or .gif file, which are the images a footer carries.");
        }

        return new OrganizationFooterTemplateImage(
            image[..separator],
            mediaType,
            await ReadAsync(file, File.ReadAllBytesAsync, cancellationToken));
    }

    private static async Task<T> ReadAsync<T>(
        FileInfo file,
        Func<string, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            return await read(file.FullName, cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"The file '{file.FullName}' could not be read: {failure.Message}", failure);
        }
    }
}
