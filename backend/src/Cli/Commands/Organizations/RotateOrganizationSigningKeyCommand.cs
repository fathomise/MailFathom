// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Mints a fresh signing key for an organization now, retiring the one it signs with.</summary>
/// <remarks>
/// It asks for no confirmation: the retired key keeps its public half and still verifies what it signed, so rotating
/// takes nothing away that a later reader needs. Rotation does not end a leaked key's validity — that is
/// 'signing-key revoke', which this command's output says.
/// </remarks>
internal static class RotateOrganizationSigningKeyCommand
{
    /// <summary>Builds the <c>organization signing-key rotate</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var organizationOption = OrganizationOptions.Organization();

        Command command = new("rotate", "Mint a fresh signing key for an organization now, retiring the one it signs with.")
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

        var rotated = await deployment.RotateOrganizationSigningKeyAsync(profile.Token, organization, cancellationToken);

        context.Console.WriteLine(
            rotated.RetiredKeyId is { } retired
                ? $"Organization {organization:D} now signs with key {rotated.ActiveKeyId:D}; key {retired:D} is retired "
                    + "and keeps its public half for what it signed. A key you suspect leaked needs 'organization signing-key revoke'."
                : $"Organization {organization:D} now signs with key {rotated.ActiveKeyId:D}.");

        return CliExitCode.Success;
    }
}
