// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Removes one of a mail account's rules.</summary>
/// <remarks>
/// Confirmed first, because nothing restores a removed rule: the archive keeps what it was for as long as its rows are
/// retained, but no command writes it back. The mail the rule already acted on stays where the rule put it.
/// </remarks>
internal static class DeleteRuleCommand
{
    /// <summary>Builds the <c>rules delete</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var ruleArgument = RuleChanges.Rule();
        var versionOption = RuleChanges.ExpectedVersion();
        var confirmationOption = CliOptions.Confirmed("removal");

        Command command = new("delete", "Remove one of a mail account's rules.")
        {
            ruleArgument,
            accountOption,
            versionOption,
            confirmationOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(ruleArgument) ?? string.Empty,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(versionOption),
            result.GetValue(confirmationOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string rule,
        string account,
        long? expectedVersion,
        bool confirmedUpFront,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var named = ConsoleSafeText.Sanitize(rule);

        if (!CliConfirmation.Agreed(
            context,
            confirmedUpFront,
            $"Removing rule '{named}' from {account} cannot be undone, and there is nobody at the terminal to confirm it. Re-run with --yes to state the agreement in the command.",
            $"Remove rule '{named}' from {account}? Nothing restores a removed rule. [y/N] "))
        {
            context.Console.WriteError("Nothing was removed.");

            return CliExitCode.Failure;
        }

        var change = await RuleChanges.ChangeNamedRuleAsync(
            context,
            AdminEndpointRoutes.RuleRemovalPath,
            rule,
            account,
            expectedVersion,
            requestedDeployment,
            position: null,
            cancellationToken);

        RuleChanges.Report(context, "Removed", change, "the rules below it moved up one position, and the mail it already acted on stays where it put it");

        return CliExitCode.Success;
    }
}
