// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Names the member of the same organization one user reports to, or names nobody.</summary>
/// <remarks>
/// The manager is the organization's statement about where a person sits, so it is written here and through the
/// administrative endpoint alone, never by the person. Who reports to somebody is read from this and never written. The
/// deployment refuses a manager that is the user, is not a person of the user's organization, or already reports to the
/// user, and the refusal names which.
/// </remarks>
internal static class SetUserManagerCommand
{
    /// <summary>Builds the <c>user set-manager</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Option<Guid?> managerOption = new("--manager")
        {
            Description = "The identifier of the member of the same organization the user reports to, as 'mfctl user list' prints it. Refused beside '--none'.",
        };

        Option<bool> noneOption = new("--none")
        {
            Description = "Name nobody as the user's manager. Refused beside '--manager'.",
        };

        Command command = new("set-manager", "Name the member of the same organization one user reports to, or nobody.")
        {
            userOption,
            managerOption,
            noneOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            ResolveManager(result.GetValue(managerOption), result.GetValue(noneOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>Settles on the manager an invocation names, refusing one that named both a manager and nobody, or neither.</summary>
    private static UserManagerRequest ResolveManager(Guid? manager, bool none) => (manager, none) switch
    {
        ({ }, true) => throw new CliFailure(
            "The invocation both names a manager and says the user has none. Drop '--none' to name the manager, or drop "
            + "'--manager' to name nobody."),
        (null, false) => throw new CliFailure(
            "The invocation names no manager. Pass '--manager' to name one, or '--none' to name nobody."),
        _ => new UserManagerRequest(manager),
    };

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        UserManagerRequest request,
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

        await deployment.SetUserManagerAsync(profile.Token, user, request, cancellationToken);

        context.Console.WriteLine(
            request.ManagerId is { } manager
                ? $"User {user:D} now reports to user {manager:D}."
                : $"User {user:D} now reports to nobody.");

        return CliExitCode.Success;
    }
}
