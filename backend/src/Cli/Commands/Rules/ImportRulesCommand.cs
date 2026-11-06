// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Brings a whole set of rules into one mail account, from a file <c>rules export</c> wrote or from a configuration's <c>MailRules</c> section.</summary>
/// <remarks>
/// <para>
/// The import is one write: the deployment judges the whole set it would produce and refuses all of it when one rule is
/// defective. <c>--mode</c> is required, because <c>replace</c> removes every rule of the account the file does not name
/// and <c>merge</c> keeps them, and neither is a safe guess. <c>--preview</c> asks the same question without writing, and
/// prints the version to state when the import is then made.
/// </para>
/// <para>
/// A rule the account does not have yet arrives disabled whatever the file says, as every created rule does, so a rule
/// able to delete mail never acts on it before somebody has looked. A rule the account already has under the same name
/// keeps whether it runs. The command reports the rules the file states enabled that are not, and names the commands that
/// preview and enable them.
/// </para>
/// </remarks>
internal static class ImportRulesCommand
{
    private const string Replace = "replace";

    private const string Merge = "merge";

    /// <summary>Builds the <c>rules import</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var versionOption = RuleChanges.ExpectedVersion();

        Option<string?> fileOption = new("--from-file", "-f")
        {
            Description =
                "The rules: a file 'rules export' wrote, or a configuration file whose MailRules section declares Rules. A JSON file, or '-' to read it from standard input.",
            Required = true,
        };

        Option<string> modeOption = new("--mode")
        {
            Description =
                "'replace' makes the account's rules exactly the file's, removing every rule the file does not name; 'merge' replaces the rules the file names and adds the rest, keeping every other rule.",
            Required = true,
        };
        modeOption.AcceptOnlyFromAmong(Replace, Merge);

        Option<bool> previewOption = new("--preview")
        {
            Description = "Report what the import would add, change, and remove, and write nothing.",
        };

        Command command = new("import", "Bring a whole set of rules into a mail account as one write. Rules it adds arrive disabled.")
        {
            accountOption,
            fileOption,
            modeOption,
            previewOption,
            versionOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new Invocation(
                result.GetValue(accountOption) ?? string.Empty,
                result.GetValue(fileOption),
                result.GetValue(modeOption) ?? string.Empty,
                result.GetValue(previewOption),
                result.GetValue(versionOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Invocation invocation,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var (origin, written) = await RuleChanges.ReadSourceAsync(context, invocation.Source, "the rules to import", cancellationToken);
        var rules = RuleImportFile.Read(written, origin, invocation.Account);
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);
        var version = await RuleChanges.VersionAsync(deployment, profile.Token, invocation.Account, invocation.ExpectedVersion, cancellationToken);

        var import = await deployment.ImportRulesAsync(
            profile.Token,
            invocation.Preview ? AdminEndpointRoutes.RuleImportPreviewPath : AdminEndpointRoutes.RuleImportPath,
            new MailRuleImportRequest(invocation.Account, version, invocation.Mode, [.. rules.Select(rule => rule.Declaration)]),
            cancellationToken);

        Report(context, invocation, rules, import);

        return CliExitCode.Success;
    }

    private static void Report(
        CliContext context,
        Invocation invocation,
        IReadOnlyList<ImportedRuleDeclaration> declared,
        MailRuleImport import)
    {
        var account = ConsoleSafeText.Sanitize(import.Account) ?? invocation.Account;
        var reported = import.Rules ?? [];

        context.Console.WriteLine(import.Committed
            ? $"Imported {declared.Count} rules into {account} ({invocation.Mode}). Its rules are now at version {import.Version}, revision {ConsoleSafeText.Sanitize(import.Revision) ?? "unreported"}."
            : $"Previewed importing {declared.Count} rules into {account} ({invocation.Mode}) against version {import.Version}. Nothing was written.");

        if (reported.Count > 0)
        {
            context.Console.WriteLine(string.Empty);

            CliTable listing = new("Rule", "Identifier", "Import", "State afterwards");

            foreach (var rule in reported)
            {
                listing.AddRow(
                    ConsoleSafeText.Sanitize(rule.Name) ?? "an unnamed rule",
                    rule.Rule is { } identifier ? $"{identifier:D}" : "assigned on import",
                    DescribeEffect(rule.Effect),
                    DescribeState(rule));
            }

            context.Console.Write(listing);
        }

        var waiting = reported
            .Take(declared.Count)
            .Zip(declared)
            .Where(pair => pair.Second.StatedEnabled && !pair.First.Enabled)
            .Select(pair => pair.First)
            .ToArray();

        if (!import.Committed)
        {
            context.Console.WriteNotice(
                $"Make the import with '{CliRootCommand.CommandName} rules import --account {invocation.Account} --from-file <file> --mode {invocation.Mode} --expected-version {import.Version}'.");
        }

        if (waiting.Length > 0)
        {
            context.Console.WriteNotice(import.Committed
                ? $"The file states these rules enabled, and they are disabled: {string.Join(", ", waiting.Select(rule => $"{rule.Rule:D}"))}. Preview each with '{CliRootCommand.CommandName} rules preview <rule> --account {invocation.Account}', then enable it with '{CliRootCommand.CommandName} rules enable <rule> --account {invocation.Account}'."
                : $"Rules the file states enabled that would be disabled after the import: {waiting.Length}. Preview and enable each once the import is made.");
        }
    }

    private static string DescribeEffect(string? effect) => effect switch
    {
        "Added" => "added",
        "Changed" => "changed",
        "Moved" => "moved",
        "Unchanged" => "unchanged",
        "Removed" => "removed",
        _ => "unreported",
    };

    private static string DescribeState(MailRuleImportedRule rule) => rule switch
    {
        { Effect: "Removed" } => rule.Enabled ? "removed; it was enabled" : "removed",
        { Enabled: true } => "enabled",
        _ => "disabled",
    };

    /// <summary>What the operator asked the command for.</summary>
    private sealed record Invocation(string Account, string? Source, string Mode, bool Preview, long? ExpectedVersion);
}
