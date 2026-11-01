// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Prints the footer template an organization's members send with, as the deployment saved it.</summary>
internal static class ShowOrganizationFooterTemplateCommand
{
    /// <summary>Builds the <c>organization footer-template show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var organizationOption = OrganizationOptions.Organization();

        Command command = new("show", "Print the footer template an organization's members send with.")
        {
            organizationOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(organizationOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid organization,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        if (await deployment.ReadOrganizationFooterTemplateAsync(profile.Token, organization, cancellationToken) is not { } template)
        {
            context.Console.WriteLine($"Organization {organization:D} sets no footer template; each member sends with their own footer.");

            return CliExitCode.Success;
        }

        context.Console.WriteLine(template.Html);

        foreach (var image in template.Images)
        {
            context.Console.WriteLine($"image {image.Name}: {image.MediaType}, {image.Content.Length} bytes");
        }

        return CliExitCode.Success;
    }
}
