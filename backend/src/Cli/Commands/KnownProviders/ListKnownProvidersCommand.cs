// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.KnownProviders;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.KnownProviders;

/// <summary>Lists the providers whose receiving server every account on their IMAP host believes by default.</summary>
internal static class ListKnownProvidersCommand
{
    /// <summary>Builds the <c>known-provider list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("list", "Read the IMAP hosts whose provider's authentication results every account on them believes.")
        {
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var providers = await deployment.ReadKnownProvidersAsync(profile.Token, cancellationToken);

        if (providers.Count == 0)
        {
            context.Console.WriteLine(
                "The deployment lists no known provider, so an account believes a server only where it names one itself.");

            return CliExitCode.Success;
        }

        context.Console.Write(Draw(providers));

        return CliExitCode.Success;
    }

    private static CliTable Draw(IReadOnlyList<KnownProviderEntry> providers)
    {
        CliTable listing = new("IMAP host", "Authserv-id", "Added by", "Added");

        foreach (var provider in providers)
        {
            listing.AddRow(
                ConsoleSafeText.Sanitize(provider.ImapHost) ?? "unreported",
                ConsoleSafeText.Sanitize(provider.AuthorityIdentifier) ?? "unreported",
                ConsoleSafeText.Sanitize(provider.AddedBy) ?? "unreported",
                $"{provider.AddedAt:u}");
        }

        return listing;
    }
}
