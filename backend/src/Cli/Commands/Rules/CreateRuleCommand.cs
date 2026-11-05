// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Adds a rule to the end of one mail account's rules, disabled.</summary>
/// <remarks>
/// The rule is saved disabled whatever the file says, so a rule able to delete mail is never acting on it before
/// anybody has seen what it would do. The command says so, and names the two commands that come next: a preview, and
/// the enabling that only <c>rules enable</c> performs.
/// </remarks>
internal static class CreateRuleCommand
{
    /// <summary>Builds the <c>rules create</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var fileOption = RuleChanges.DeclarationFile();
        var versionOption = RuleChanges.ExpectedVersion();

        Command command = new("create", "Add a rule to the end of a mail account's rules. It is saved disabled; preview it, then enable it.")
        {
            accountOption,
            fileOption,
            versionOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(fileOption),
            result.GetValue(versionOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string account,
        string? source,
        long? expectedVersion,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var declaration = await RuleChanges.ReadDeclarationAsync(context, source, cancellationToken);
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);
        var version = await RuleChanges.VersionAsync(deployment, profile.Token, account, expectedVersion, cancellationToken);

        var change = await deployment.ChangeRulesAsync(
            profile.Token,
            AdminEndpointRoutes.RulesPath,
            new MailRuleChangeRequest(account, version, Declaration: declaration),
            cancellationToken);

        RuleChanges.Report(context, "Created", change, "it is disabled at the end of the account's rules, so no pass applies it yet");
        context.Console.WriteNotice(
            $"See what it would do with '{CliRootCommand.CommandName} rules preview {change.Rule:D} --account {account}', then enable it with '{CliRootCommand.CommandName} rules enable {change.Rule:D} --account {account}'.");

        return CliExitCode.Success;
    }
}
