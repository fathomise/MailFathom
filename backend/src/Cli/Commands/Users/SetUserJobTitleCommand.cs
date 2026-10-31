// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Writes the title one user's colleagues read them by on their member contact, or takes it off.</summary>
/// <remarks>
/// The title is the organization's statement about where a person sits, so it is written here and through the
/// administrative endpoint alone, never by the person. It asks for no confirmation, for the reason a rename asks none:
/// nothing is keyed by it.
/// </remarks>
internal static class SetUserJobTitleCommand
{
    /// <summary>Builds the <c>user set-job-title</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Option<string?> jobTitleOption = new("--job-title")
        {
            Description = "The title the user's colleagues read them by, at most 256 characters. Refused beside '--none'.",
        };

        Option<bool> noneOption = new("--none")
        {
            Description = "Take the user's title off. Refused beside '--job-title'.",
        };

        Command command = new("set-job-title", "Write the title one user's colleagues read them by, or take it off.")
        {
            userOption,
            jobTitleOption,
            noneOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            ResolveJobTitle(result.GetValue(jobTitleOption), result.GetValue(noneOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>Settles on the title an invocation names, refusing one that named both a title and none, or neither.</summary>
    private static UserJobTitleRequest ResolveJobTitle(string? jobTitle, bool none) => (string.IsNullOrWhiteSpace(jobTitle), none) switch
    {
        (false, true) => throw new CliFailure(
            "The invocation both names a title and says the user has none. Drop '--none' to write the title, or drop "
            + "'--job-title' to take it off."),
        (true, false) => throw new CliFailure(
            "The invocation names no title. Pass '--job-title' to write one, or '--none' to take the user's title off."),
        _ => new UserJobTitleRequest(none ? null : jobTitle!.Trim()),
    };

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        UserJobTitleRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(
            deployment,
            profile.Token,
            requestedUser,
            cancellationToken);

        await deployment.SetUserJobTitleAsync(profile.Token, user, request, cancellationToken);

        context.Console.WriteLine(
            request.JobTitle is { } jobTitle
                ? $"User {user:D} now carries the title {jobTitle}."
                : $"User {user:D} now carries no title.");

        return CliExitCode.Success;
    }
}
