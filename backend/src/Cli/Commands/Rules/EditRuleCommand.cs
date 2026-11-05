// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Replaces what one of a mail account's rules declares, keeping its identifier, its position, and whether it runs.</summary>
/// <remarks>
/// The file is the rule as a whole rather than a patch of it, so what the rule declares afterwards is exactly what the
/// operator wrote. A rule that was enabled stays enabled, and the deployment judges the new declaration as it would a
/// created one before anything is written.
/// </remarks>
internal static class EditRuleCommand
{
    /// <summary>Builds the <c>rules edit</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var ruleArgument = RuleChanges.Rule();
        var fileOption = RuleChanges.DeclarationFile();
        var versionOption = RuleChanges.ExpectedVersion();

        Command command = new("edit", "Replace what one of a mail account's rules declares, keeping its identifier, its position, and whether it runs.")
        {
            ruleArgument,
            accountOption,
            fileOption,
            versionOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(ruleArgument) ?? string.Empty,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(fileOption),
            result.GetValue(versionOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string rule,
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
            AdminEndpointRoutes.RuleReplacementPath,
            new MailRuleChangeRequest(account, version, rule, declaration),
            cancellationToken);

        RuleChanges.Report(context, "Replaced the declaration of", change, "its position and whether it runs are as they were");

        return CliExitCode.Success;
    }
}
