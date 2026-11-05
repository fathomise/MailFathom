// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Reports one stored rule of a mail account in full: its condition, what runs it, and what a match does.</summary>
/// <remarks>
/// It reads the account's whole set and chooses from it rather than asking the deployment for one rule. A rule means
/// what it does partly because of its position — which rule reaches a message first is a property of the set — so the
/// deployment publishes the set and choosing is this command's half of the work. It also means a rule named after one
/// of the deployment's own routes is reachable like any other.
/// </remarks>
internal static class ShowRuleCommand
{
    /// <summary>Builds the <c>rules show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Argument<string> nameArgument = new("name")
        {
            Description = "The name of the rule to show.",
        };

        Command command = new("show", "Report one of a mail account's rules: its condition, what runs it, and what a match does.")
        {
            nameArgument,
            accountOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(nameArgument) ?? string.Empty,
            result.GetValue(accountOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string name,
        string account,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var ruleSet = await new AdminApiClient(transport, context.Console)
            .ReadRulesAsync(profile.Token, account, cancellationToken);

        // Names are compared without regard to case for the reason a set refuses two of them that way: an account
        // cannot hold two rules an operator would read as one, so neither can a lookup find two.
        var rule = ruleSet.Rules?.FirstOrDefault(
            candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            throw new CliFailure(
                $"{account} has no rule named '{name}'. Run '{CliRootCommand.CommandName} rules list --account {account}' to see the ones it has.");
        }

        WriteRule(context, ruleSet, rule);

        return CliExitCode.Success;
    }

    private static void WriteRule(CliContext context, LoadedRuleSet ruleSet, LoadedRule rule)
    {
        CliDetails details = new();
        details.Add("Rule", $"{rule.Name ?? "an unnamed rule"} ({rule.Id:D})");
        details.Add("State", $"{rule.DescribeState()}, position {rule.Position}");
        details.Add("Rule set", $"{ruleSet.Revision ?? "unreported"}, version {ruleSet.Version}");
        details.Add("Condition", rule.Condition ?? "unreported");
        details.Add("Runs on", rule.DescribeTriggers());
        details.Add("A match", rule.DescribeActions());

        context.Console.Write(details);

        if (ruleSet.Held)
        {
            context.Console.WriteLine(ListRulesCommand.DescribeHold(ruleSet));
        }
    }
}
