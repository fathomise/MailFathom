// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>What every command changing one account's rules shares: how a rule and a rule object are named, the version a change states, and what is reported afterwards.</summary>
/// <remarks>
/// <para>
/// A change states the version of the account's rules it was decided against, and the deployment refuses it as a
/// conflict when the rules have moved since. An operator who read the rules with <c>rules list</c> passes the version it
/// printed; one who does not is decided for at the version the rules stand at when the command runs, which still turns
/// a write landing between that reading and this change into a conflict rather than a silent overwrite.
/// </para>
/// <para>
/// What a change prints is the account, the rule by its name and its identifier, and the version and revision the rules
/// stand at afterwards — enough that a command that looked successful can be checked against <c>rules list</c> — and
/// nothing a rule declares. A refusal is the deployment's own sentence, which names each defective rule by its
/// identifier rather than quoting it.
/// </para>
/// </remarks>
internal static class RuleChanges
{
    /// <summary>The value of <c>--from-file</c> that reads the rule object from standard input.</summary>
    internal const string StandardInput = "-";

    /// <summary>Builds the argument naming the rule a command acts on.</summary>
    /// <returns>The argument.</returns>
    internal static Argument<string> Rule() => new("rule")
    {
        Description = "The rule, by its identifier or by its name within the account.",
    };

    /// <summary>Builds the option naming where a rule object is read from.</summary>
    /// <returns>The option.</returns>
    internal static Option<string?> DeclarationFile() => new("--from-file", "-f")
    {
        Description =
            "The rule object: Name, Condition, Triggers, Schedule, Actions, and StopWhenMatched, with no Accounts or Enabled. "
            + "A JSON file, or '-' to read it from standard input.",
    };

    /// <summary>Builds the option stating the version of the account's rules a change was decided against.</summary>
    /// <returns>The option.</returns>
    internal static Option<long?> ExpectedVersion() => new("--expected-version")
    {
        Description =
            "The version of the account's rules this change was decided against, as 'mfctl rules list' prints it. A change is refused if the rules have moved past it. Defaults to the version they are at when the command runs.",
    };

    /// <summary>Reads the rule object an invocation named, as the JSON object the deployment is handed.</summary>
    /// <param name="context">What the command needs from its surroundings, which is where standard input is read.</param>
    /// <param name="source">A path, or <see cref="StandardInput" />.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rule object.</returns>
    /// <exception cref="CliFailure">Thrown when nothing was named, the file cannot be read, or it holds no JSON object.</exception>
    /// <remarks>Nothing it reads is echoed in a refusal: the file is named by its path, and a document that is not JSON is reported by where it stops being JSON.</remarks>
    internal static async Task<JsonElement> ReadDeclarationAsync(CliContext context, string? source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new CliFailure("Name the rule object with --from-file, or with --from-file - to read it from standard input.");
        }

        var origin = source == StandardInput ? "Standard input" : Path.GetFullPath(source);
        var written = source == StandardInput
            ? await context.Console.ReadInputAsync(cancellationToken)
            : await ReadFileAsync(origin, cancellationToken);

        if (string.IsNullOrWhiteSpace(written))
        {
            throw new CliFailure($"{origin} is empty, so it declares no rule.");
        }

        return Parse(written, origin);
    }

    /// <summary>Reports the version a change states: the one the operator gave, or the one the rules stand at now.</summary>
    /// <param name="deployment">The deployment to read the rules from where no version was given.</param>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="account">The account whose rules change.</param>
    /// <param name="expected">The version the operator gave, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The version.</returns>
    internal static async Task<long> VersionAsync(
        AdminApiClient deployment,
        string token,
        string account,
        long? expected,
        CancellationToken cancellationToken) =>
        expected ?? (await deployment.ReadRulesAsync(token, account, cancellationToken)).Version;

    /// <summary>Sends a change naming one existing rule and carrying no rule object: enabling, disabling, moving, or removing it.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="path">The route of the change.</param>
    /// <param name="rule">The rule, by its identifier or its name.</param>
    /// <param name="account">The account whose rules change.</param>
    /// <param name="expectedVersion">The version the operator gave, or <see langword="null" /> for the one the rules stand at now.</param>
    /// <param name="requestedDeployment">The deployment the operator named for this invocation, if any.</param>
    /// <param name="position">Where a move puts the rule; <see langword="null" /> for every other change.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>What changed.</returns>
    internal static async Task<MailRuleChange> ChangeNamedRuleAsync(
        CliContext context,
        string path,
        string rule,
        string account,
        long? expectedVersion,
        string? requestedDeployment,
        int? position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);
        var version = await VersionAsync(deployment, profile.Token, account, expectedVersion, cancellationToken);

        return await deployment.ChangeRulesAsync(
            profile.Token,
            path,
            new MailRuleChangeRequest(account, version, rule, Position: position),
            cancellationToken);
    }

    /// <summary>Reports a change that committed, naming the account, the rule, and where the rules stand now.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="what">What happened, as a sentence that ends before the rule is named, such as <c>Enabled</c>.</param>
    /// <param name="change">What the deployment reported.</param>
    /// <param name="consequence">What the change means for the account's mail, as a clause, or <see langword="null" />.</param>
    internal static void Report(CliContext context, string what, MailRuleChange change, string? consequence = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(change);

        var account = ConsoleSafeText.Sanitize(change.Account) ?? "the account";

        context.Console.WriteLine(
            $"{what} rule '{ConsoleSafeText.Sanitize(change.Name) ?? "unnamed"}' ({change.Rule:D}) in {account}{(consequence is null ? "." : $": {consequence}.")}");
        context.Console.WriteLine(
            $"The rules of {account} are now at version {change.Version}, revision {ConsoleSafeText.Sanitize(change.Revision) ?? "unreported"}.");
    }

    private static async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new CliFailure($"There is no file at {path} to read the rule from.");
        }

        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"{path} could not be read, so nothing was sent.", failure);
        }
    }

    private static JsonElement Parse(string json, string origin)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : throw new CliFailure($"{origin} holds JSON that is not an object, and a rule is one object.");
        }
        catch (JsonException failure)
        {
            throw new CliFailure(
                $"{origin} is not a JSON document, so nothing was sent. It stops being one at line {(failure.LineNumber ?? 0) + 1}, byte {(failure.BytePositionInLine ?? 0) + 1}.",
                failure);
        }
    }
}
