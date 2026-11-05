// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Reports one mail account's stored rules, in the order they run, and whether the set is held.</summary>
/// <remarks>
/// A set is held when an enabled rule no longer compiles — a condition bound tightened, a fact withdrawn by a release —
/// and then no pass runs any of the account's rules. That is the right behavior and a quiet one, so the listing says
/// so before it lists anything.
/// </remarks>
internal static class ListRulesCommand
{
    /// <summary>Builds the <c>rules list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Command command = new("list", "Report one mail account's rules, in the order they run.")
        {
            accountOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>States whether a pass runs the set, naming the rules that hold it where one does.</summary>
    /// <param name="ruleSet">The account's stored set.</param>
    /// <returns>One line an operator reads.</returns>
    internal static string DescribeHold(LoadedRuleSet ruleSet) => ruleSet.Held
        ? $"Held: no pass runs these rules, because {string.Join(", ", ruleSet.UncompiledRules ?? [])} no longer compile. Change or remove those rules to release the set."
        : "Running: every enabled rule below is applied.";

    private static async Task<int> RunAsync(
        CliContext context,
        string account,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var ruleSet = await new AdminApiClient(transport, context.Console)
            .ReadRulesAsync(profile.Token, account, cancellationToken);

        WriteRuleSet(context, account, ruleSet);

        return CliExitCode.Success;
    }

    /// <summary>Writes the set as an operator reads it: whether it runs, then the rules in the order they run.</summary>
    private static void WriteRuleSet(CliContext context, string account, LoadedRuleSet ruleSet)
    {
        context.Console.WriteLine($"{account} — rule set {ConsoleSafeText.Sanitize(ruleSet.Revision) ?? "unreported"}, version {ruleSet.Version}");
        context.Console.WriteLine(DescribeHold(ruleSet));

        if (ruleSet.Rules is not { Count: > 0 } rules)
        {
            context.Console.WriteLine("This account has no rules, so nothing is applied to its mail.");

            return;
        }

        context.Console.WriteLine(string.Empty);

        CliTable listing = new("Rule", "State", "Runs on", "A match");

        foreach (var rule in rules)
        {
            listing.AddRow(
                ConsoleSafeText.Sanitize(rule.Name) ?? "an unnamed rule",
                rule.DescribeState(),
                rule.DescribeTriggers(),
                rule.DescribeActions());
        }

        context.Console.Write(listing);
    }
}
