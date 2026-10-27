// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.KnownProviders;

namespace MailFathom.Cli.Commands.KnownProviders;

/// <summary>Adds a provider whose receiving server every account on its IMAP host believes by default.</summary>
/// <remarks>
/// An entry is a security decision: a provider that lets a forged header carrying its own authserv-id through lets anybody
/// claim a passing verdict under it. So the command states the three things an entry rests on and asks before writing,
/// and the deployment refuses a host or an authserv-id no match could be made against.
/// </remarks>
internal static class AddKnownProviderCommand
{
    /// <summary>What the operator is asked to have verified before an entry is written.</summary>
    internal const string Requirements =
        "An entry is safe only where all three of these hold, verified against mail actually delivered:\n"
        + "  1. The provider's receiving server removes or renames every incoming Authentication-Results header carrying\n"
        + "     its own authserv-id. Send a message from outside the provider carrying a forged header with that\n"
        + "     authserv-id claiming dkim=pass and dmarc=pass for a failing From domain; in the copy read through this\n"
        + "     IMAP host it must be gone or renamed, and the topmost header must be the provider's own.\n"
        + "  2. The authserv-id never changes: it is identical on at least three messages from different senders\n"
        + "     delivered on different days.\n"
        + "  3. Every message reaching the mailbox from outside passes through that server.\n"
        + "Every account reading mail from this host and naming no server of its own will believe it from the next\n"
        + "extraction on. Mail already stored is re-read only by 'mfctl mailbox rederive'.";

    /// <summary>Builds the <c>known-provider add</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var hostOption = KnownProviderOptions.ImapHost();
        var authorityOption = new Option<string>("--authserv-id")
        {
            Description = "The authserv-id that provider's receiving server writes into every Authentication-Results header it adds.",
            Required = true,
        };
        var confirmedOption = CliOptions.Confirmed("addition");

        Command command = new("add", "List a provider whose receiving server every account on its IMAP host believes.")
        {
            hostOption,
            authorityOption,
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new KnownProviderRequest(result.GetValue(hostOption)!, result.GetValue(authorityOption)!),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        KnownProviderRequest request,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        context.Console.WriteWarning(Requirements);

        if (!CliConfirmation.Agreed(
                context,
                confirmedUpFront,
                "There is nobody at the terminal to confirm the provider was verified. Pass --yes once it has been.",
                "Have all three been verified for this provider? [y/N] "))
        {
            context.Console.WriteError("Nothing was added.");

            return CliExitCode.Failure;
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var added = await deployment.AddKnownProviderAsync(profile.Token, request, cancellationToken);

        context.Console.WriteLine(
            $"Accounts on {ConsoleSafeText.Sanitize(added.ImapHost) ?? "the host"} that name no server of their own now believe "
            + $"{ConsoleSafeText.Sanitize(added.AuthorityIdentifier) ?? "the listed server"}.");

        return CliExitCode.Success;
    }
}
