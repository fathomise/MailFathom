// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Organizations;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Turns one organization's External mark on or off for every member at once.</summary>
/// <remarks>
/// The mark changes what members see beside a message and nothing about the message, its sender mark, or any stored
/// verdict, so turning it either way asks for no confirmation. The value is stated rather than toggled, so running the
/// command twice leaves the organization where the operator said.
/// </remarks>
internal static class SetOrganizationExternalMarkCommand
{
    /// <summary>Builds the <c>organization set-external-mark</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var organizationOption = OrganizationOptions.Organization();

        Option<bool> enabledOption = new("--enabled")
        {
            Description = "true to mark every received message the organization's signature does not prove as External, false to stop.",
            Required = true,
        };

        Command command = new(
            "set-external-mark",
            "Turn the External mark on or off for every member of one organization.")
        {
            organizationOption,
            enabledOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(organizationOption),
            result.GetValue(enabledOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid organization,
        bool enabled,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.SetOrganizationExternalMarkAsync(
            profile.Token,
            organization,
            new OrganizationExternalMarkRequest(enabled),
            cancellationToken);

        context.Console.WriteLine(enabled
            ? $"Organization {organization:D} now marks received mail its signature does not prove as External."
            : $"Organization {organization:D} no longer marks any mail as External.");

        return CliExitCode.Success;
    }
}
