// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.KnownProviders;

/// <summary>Removes the provider listed for one IMAP host, so accounts on it believe no server they do not name themselves.</summary>
internal static class RemoveKnownProviderCommand
{
    /// <summary>Builds the <c>known-provider remove</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var hostOption = KnownProviderOptions.ImapHost();

        Command command = new("remove", "Stop believing the provider listed for one IMAP host.")
        {
            hostOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(hostOption)!,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string imapHost,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.RemoveKnownProviderAsync(profile.Token, imapHost, cancellationToken);

        context.Console.WriteLine(
            "Removed. Accounts on that host believe no server they do not name themselves from the next extraction on; "
            + "'mfctl mailbox rederive' re-reads mail already stored.");

        return CliExitCode.Success;
    }
}
