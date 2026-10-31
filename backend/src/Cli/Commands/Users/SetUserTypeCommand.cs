// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Marks one user a person or a technical account.</summary>
/// <remarks>
/// A service account has no member contact in its organization's directory and is never suggested to a colleague, so
/// this is how a technical user is taken out of the directory. Every user is recorded as a person, so this is the step
/// an operator takes for each technical user once.
/// </remarks>
internal static class SetUserTypeCommand
{
    /// <summary>The types a user can be, as the deployment spells them.</summary>
    private static readonly string[] Types = ["User", "ServiceAccount"];

    /// <summary>Builds the <c>user set-type</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Option<string> typeOption = new("--type")
        {
            Description = "'User' for a person, or 'ServiceAccount' for a technical account, which leaves the member directory.",
            Required = true,
        };
        typeOption.AcceptOnlyFromAmong(Types);

        Command command = new("set-type", "Mark one user a person or a technical account.")
        {
            userOption,
            typeOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(typeOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        string type,
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

        await deployment.SetUserTypeAsync(profile.Token, user, new UserTypeRequest(type), cancellationToken);

        context.Console.WriteLine($"User {user:D} is now of type {type}.");

        return CliExitCode.Success;
    }
}
