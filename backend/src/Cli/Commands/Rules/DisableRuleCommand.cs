// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Disables one of a mail account's rules, keeping it where it is without any pass applying it.</summary>
/// <remarks>Always a write that commits, even for a rule that no longer compiles, so disabling is how a set held by one broken rule is released.</remarks>
internal static class DisableRuleCommand
{
    /// <summary>Builds the <c>rules disable</c> command.</summary>
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

        Command command = new("disable", "Disable one of a mail account's rules, keeping it without any pass applying it.")
        {
            ruleArgument,
            accountOption,
            versionOption,
            endpointOption,
        };

        command.SetAction(async (result, cancellationToken) =>
        {
            var change = await RuleChanges.ChangeNamedRuleAsync(
                context,
                AdminEndpointRoutes.RuleDisablementPath,
                result.GetValue(ruleArgument) ?? string.Empty,
                result.GetValue(accountOption) ?? string.Empty,
                result.GetValue(versionOption),
                CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
                position: null,
                cancellationToken);

            RuleChanges.Report(context, "Disabled", change, "it is kept where it is, and no pass applies it");

            return CliExitCode.Success;
        });

        return command;
    }
}
