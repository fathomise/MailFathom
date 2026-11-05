// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Moves one of a mail account's rules to another position in the order its rules are evaluated in.</summary>
/// <remarks>The position is counted from 0, as <c>rules show</c> prints it, so the first rule a pass reaches is at 0 and a rule that ends the pass keeps every rule below it from the mail it matches.</remarks>
internal static class MoveRuleCommand
{
    /// <summary>Builds the <c>rules move</c> command.</summary>
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

        Option<int> positionOption = new("--to")
        {
            Description = "The position the rule goes to, counted from 0 for the first rule a pass evaluates.",
            Required = true,
        };

        Command command = new("move", "Move one of a mail account's rules to another position in the order its rules are evaluated in.")
        {
            ruleArgument,
            accountOption,
            positionOption,
            versionOption,
            endpointOption,
        };

        command.SetAction(async (result, cancellationToken) =>
        {
            var position = result.GetValue(positionOption);
            var change = await RuleChanges.ChangeNamedRuleAsync(
                context,
                AdminEndpointRoutes.RulePositionPath,
                result.GetValue(ruleArgument) ?? string.Empty,
                result.GetValue(accountOption) ?? string.Empty,
                result.GetValue(versionOption),
                CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
                position,
                cancellationToken);

            RuleChanges.Report(context, "Moved", change, $"it is now at position {position}");

            return CliExitCode.Success;
        });

        return command;
    }
}
