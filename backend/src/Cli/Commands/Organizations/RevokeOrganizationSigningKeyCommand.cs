// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Revokes one of an organization's signing keys, so nothing it signed is to be trusted from then on.</summary>
/// <remarks>
/// It asks first, as every command here that cannot be undone does: a revoked key stays revoked. Revoking the active key
/// mints its replacement in the same act where the key ring allows, and the output names it.
/// </remarks>
internal static class RevokeOrganizationSigningKeyCommand
{
    /// <summary>Builds the <c>organization signing-key revoke</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var organizationOption = OrganizationOptions.Organization();
        var keyOption = new Option<Guid>("--key")
        {
            Description = "The signing key to revoke, by the identifier 'organization signing-key list' reports.",
            Required = true,
        };
        var confirmedOption = CliOptions.Confirmed("revocation");

        Command command = new("revoke", "Revoke one of an organization's signing keys, minting a replacement when it is the active one.")
        {
            organizationOption,
            keyOption,
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(organizationOption),
            result.GetValue(keyOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid organization,
        Guid key,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        if (!CliConfirmation.Agreed(
                context,
                confirmedUpFront,
                "There is nobody at the terminal to agree to this, and a revoked key cannot be put back. Pass --yes to revoke without being asked.",
                $"Revoke signing key {key:D} of organization {organization:D}? A revoked key is never trusted again for anything it signed. [y/N] "))
        {
            context.Console.WriteError("Nothing was revoked.");

            return CliExitCode.Failure;
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var revoked = await deployment.RevokeOrganizationSigningKeyAsync(profile.Token, organization, key, cancellationToken);

        context.Console.WriteLine((revoked.WasActive, revoked.ActiveKeyId) switch
        {
            (true, { } replacement) => $"Revoked signing key {key:D}. Organization {organization:D} now signs with key {replacement:D}.",
            (true, null) => $"Revoked signing key {key:D}. No replacement could be sealed, because no key ring is "
                + $"configured or its active key could not be resolved, so organization {organization:D} holds no active key "
                + "until the deployment's hourly pass mints one.",
            _ => $"Revoked retired signing key {key:D}; the organization's active key is unchanged.",
        });

        return CliExitCode.Success;
    }
}
