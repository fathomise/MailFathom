// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Classification;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Classification;

/// <summary>Asks a deployment to go over every message it already holds for one account: spam first, then categories.</summary>
/// <remarks>
/// <para>
/// The command an operator runs after switching classification or categories on, widening what they cover, or switching
/// filing on. Mail is decided as it arrives, so none of those reaches the mail already there until somebody asks — and this
/// is that asking.
/// </para>
/// <para>
/// <strong>It is a dry run unless <c>--apply</c> is given.</strong> With filing switched on, a run over an inbox is the
/// largest single thing this feature can do to somebody's mail, so the default reports what it would do and touches
/// nothing on the mail server. Verdicts and categories are recorded either way, because both are derived data rather than
/// a change to a mailbox.
/// </para>
/// <para>
/// It returns as soon as the deployment has written the request down, and never waits for the walk. The run is carried
/// by the account's synchronization runs, so this terminal is not what keeps it alive and closing it cannot cancel one;
/// <c>classification run-status</c> is where the run is watched from.
/// </para>
/// </remarks>
internal static class ClassificationRunCommand
{
    /// <summary>Builds the <c>classification run</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Option<string[]> foldersOption = new("--folder")
        {
            Description = "Go over only this folder, named by its alias. Repeatable. Defaults to every folder either function covers.",
            AllowMultipleArgumentsPerToken = true,
        };

        Option<bool> applyOption = new("--apply")
        {
            Description = "Carry out what the switches ask for. Without it the run reports what it would do and changes nothing.",
        };

        Option<bool> rescoreOption = new("--rescore")
        {
            Description = "Ask about mail again even where its verdict and categories already answer the settings now in force.",
        };

        Option<string?> categoryOption = new("--category")
        {
            Description = "Apply only this one own category, asking one question per message and nothing about spam. Refused beside --apply or --rescore.",
        };

        Command command = new("run", "Go over every message the deployment already holds for an account: spam first, then the categories of what is not spam.")
        {
            accountOption,
            foldersOption,
            applyOption,
            rescoreOption,
            categoryOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new ClassificationRunRequest(
                result.GetValue(accountOption) ?? string.Empty,
                // An option left out reads as an empty array, and the deployment refuses an empty list as naming nothing.
                result.GetValue(foldersOption) is { Length: > 0 } folders ? folders : null,
                result.GetValue(applyOption),
                result.GetValue(rescoreOption),
                result.GetValue(categoryOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        ClassificationRunRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var started = await new AdminApiClient(transport, context.Console)
            .StartClassificationRunAsync(profile.Token, request, cancellationToken);

        context.Console.WriteLine(started.Started
            ? $"A classification run over {request.Account} has been asked for."
            : $"A classification run over {request.Account} was already under way, so nothing new was started and the terms you asked for were not applied to it.");

        if (started.Run is { } run)
        {
            CliDetails details = new();
            details.Add("Spam folders", string.Join(", ", run.SpamFolders));
            details.Add("Category folders", string.Join(", ", run.CategoryFolders));
            details.Add("Acting", DescribePosture(run));
            details.Add("Progress", run.DescribeProgress());

            context.Console.Write(details);
        }

        context.Console.WriteLine(
            $"The run is carried by the account's synchronization runs. Watch it with '{CliRootCommand.CommandName} classification run-status --account {request.Account}'.");

        return CliExitCode.Success;
    }

    /// <summary>States what the run will do to the mailbox, in the words an operator has to be able to act on.</summary>
    /// <remarks>
    /// A dry run says so and says how to ask for the other one, because the whole point of the default is that somebody
    /// reads the report before the mail moves. A run applying one category has no other one to ask for, because the
    /// deployment refuses that option beside <c>--apply</c>.
    /// </remarks>
    private static string DescribePosture(ClassificationRun run) => run switch
    {
        { Category: not null } => "no — a run applying one category records that category and takes no action on the mailbox.",
        { IsDryRun: true } => "no — this is a dry run; it records verdicts and categories and leaves the mailbox alone. Add --apply to carry out what the switches ask for.",
        _ => "yes — what the deployment's switches ask for is written down and carried out by the account's convergence pass.",
    };
}
