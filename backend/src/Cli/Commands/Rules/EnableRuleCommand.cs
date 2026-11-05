// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Enables one of a mail account's rules, so the next pass applies it.</summary>
/// <remarks>
/// The only command that makes a rule act on mail, which is why creating one never does. The deployment judges the rule
/// again as it enables it, so a rule whose folder or permission was withdrawn since it was written is refused here
/// rather than discovered by a pass.
/// </remarks>
internal static class EnableRuleCommand
{
    /// <summary>Builds the <c>rules enable</c> command.</summary>
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

        Command command = new("enable", "Enable one of a mail account's rules, so the next pass applies it to the account's mail.")
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
                AdminEndpointRoutes.RuleEnablementPath,
                result.GetValue(ruleArgument) ?? string.Empty,
                result.GetValue(accountOption) ?? string.Empty,
                result.GetValue(versionOption),
                CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
                position: null,
                cancellationToken);

            RuleChanges.Report(context, "Enabled", change, "the next pass applies it to arriving mail and to any run asked for");

            return CliExitCode.Success;
        });

        return command;
    }
}
