// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Organizations;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Organizations;

/// <summary>Reads the keys one organization signs its accounts' mail with, oldest first.</summary>
/// <remarks>
/// Identifiers and instants only: the deployment never sends key material, so there is nothing here to keep off a
/// terminal. The listing is where 'signing-key revoke' takes its key identifier from, and where an operator sees whether
/// the organization has an active key at all — one created while the deployment had no key ring has none.
/// </remarks>
internal static class ListOrganizationSigningKeysCommand
{
    /// <summary>Builds the <c>organization signing-key list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var organizationOption = OrganizationOptions.Organization();

        Command command = new("list", "Read the keys one organization signs its accounts' mail with.")
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

        var listing = await deployment.ReadOrganizationSigningKeysAsync(profile.Token, organization, cancellationToken);
        var name = ConsoleSafeText.Sanitize(listing.DisplayName) ?? "unreported";

        if (listing.Keys is not { Count: > 0 } keys)
        {
            context.Console.WriteLine(
                $"Organization {organization:D} ({name}) holds no signing key, so it has nothing to sign with. The "
                + "deployment mints one once a data-encryption key ring is configured, or now with 'organization "
                + "signing-key rotate'.");

            return CliExitCode.Success;
        }

        context.Console.WriteLine($"Signing keys of organization {organization:D} ({name}), oldest first:");
        context.Console.Write(Draw(keys));

        return CliExitCode.Success;
    }

    private static CliTable Draw(IReadOnlyList<OrganizationSigningKeyEntry> keys)
    {
        CliTable listing = new("Key", "State", "Created", "Retired", "Revoked");

        foreach (var key in keys)
        {
            listing.AddRow(
                $"{key.KeyId:D}",
                ConsoleSafeText.Sanitize(key.State) ?? "unreported",
                $"{key.CreatedAt:u}",
                key.RetiredAt is { } retiredAt ? $"{retiredAt:u}" : "-",
                key.RevokedAt is { } revokedAt ? $"{revokedAt:u}" : "-");
        }

        return listing;
    }
}
