// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Writes one mail account's rules to standard output as the file <c>rules import</c> reads.</summary>
/// <remarks>
/// The file is the rules in the order they run, each as the rule object with <c>Enabled</c> beside it, and nothing else:
/// no account, no identifier, no version, and no secret. It is written to standard output rather than to a path, so it
/// can be redirected into a file, piped into another deployment's import, or read as a diff.
/// </remarks>
internal static class ExportRulesCommand
{
    /// <summary>Writes the file indented and with a condition's quotes and letters as typed, so it reads as a diff.</summary>
    /// <remarks>
    /// The relaxed encoder still escapes every control character, so nothing a rule carries can act on the terminal; it
    /// only leaves alone what the default encoder escapes for embedding in a web page, which this file never is.
    /// </remarks>
    private static readonly JsonTypeInfo<ExportedRuleSet> FileFormat = (JsonTypeInfo<ExportedRuleSet>)new JsonSerializerOptions(CliJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }.GetTypeInfo(typeof(ExportedRuleSet));

    /// <summary>Builds the <c>rules export</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Command command = new("export", "Write a mail account's rules, in order and with their enabled state, as the JSON file 'rules import' reads.")
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

    private static async Task<int> RunAsync(
        CliContext context,
        string account,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var exported = await new AdminApiClient(transport, context.Console)
            .ExportRulesAsync(profile.Token, account, cancellationToken);

        context.Console.WriteLine(JsonSerializer.Serialize(exported with { Rules = exported.Rules ?? [] }, FileFormat));

        return CliExitCode.Success;
    }
}
