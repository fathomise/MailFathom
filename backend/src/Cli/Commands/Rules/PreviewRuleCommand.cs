// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Shows which of a mail account's stored messages one rule would match, and what it would ask for, changing nothing.</summary>
/// <remarks>
/// <para>
/// The step between creating a rule and enabling it. It previews either a stored rule — disabled or not, which is what
/// a created rule is — or a rule object not yet written, judged exactly as a write would judge it first. Nothing is
/// asked of the mail server and nothing is recorded.
/// </para>
/// <para>
/// One page evaluates a bounded slice of the account's mail, and the command prints the cursor the next page continues
/// from, as the history does. The messages are named by their stored identity, which every other read of them uses,
/// and nothing they say is printed.
/// </para>
/// </remarks>
internal static class PreviewRuleCommand
{
    /// <summary>Builds the <c>rules preview</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var fileOption = RuleChanges.DeclarationFile();
        var cursorOption = CliOptions.Cursor();

        Argument<string?> ruleArgument = new("rule")
        {
            Description = "A stored rule, by its identifier or by its name. Leave it out to preview the rule object --from-file names instead.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        Command command = new("preview", "Show which of a mail account's messages one rule would match and what it would ask for, changing nothing.")
        {
            ruleArgument,
            accountOption,
            fileOption,
            cursorOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(ruleArgument),
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(fileOption),
            result.GetValue(cursorOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? rule,
        string account,
        string? source,
        string? cursor,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var namesRule = !string.IsNullOrWhiteSpace(rule);

        if (namesRule == !string.IsNullOrWhiteSpace(source))
        {
            throw new CliFailure("Name one stored rule, or one rule object with --from-file, and not both.");
        }

        Guid? after = null;

        if (cursor is not null)
        {
            after = Guid.TryParse(cursor, out var continuation)
                ? continuation
                : throw new CliFailure("The cursor is not one a preview printed. Leave it out to start from the first message.");
        }

        JsonElement? declaration = namesRule ? null : await RuleChanges.ReadDeclarationAsync(context, source, cancellationToken);
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var page = await new AdminApiClient(transport, context.Console).PreviewRuleAsync(
            profile.Token,
            new MailRulePreviewPageRequest(account, namesRule ? rule : null, declaration, after),
            cancellationToken);

        WritePage(context, account, namesRule ? $"rule '{ConsoleSafeText.Sanitize(rule)}'" : "the rule object", page);

        return CliExitCode.Success;
    }

    private static void WritePage(CliContext context, string account, string previewed, MailRulePreviewPage page)
    {
        var matches = page.Matches ?? [];

        context.Console.WriteLine(
            $"Previewing {previewed} over {account}: {page.ExaminedEmailCount} messages examined, {matches.Count} would match, {page.FailedEmailCount} gave the condition no answer. Nothing was changed.");
        context.Console.WriteLine(
            $"A match would ask for: {(page.Requested is { Count: > 0 } requested ? string.Join(", ", requested.Select(action => action.Describe())) : "nothing")}.");

        if (matches.Count > 0)
        {
            context.Console.WriteLine(string.Empty);

            CliTable listing = new("Message");

            foreach (var email in matches)
            {
                listing.AddRow($"{email:D}");
            }

            context.Console.Write(listing);
        }

        if (page.ContinueAfter is { } continueAfter)
        {
            context.Console.WriteLine($"More of the account's mail remains. Continue with --cursor {continueAfter:D}");
        }
    }
}
