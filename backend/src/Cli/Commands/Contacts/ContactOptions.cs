// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Contacts;
using MailFathom.Cli.Commands.Users;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>The options the contact commands share.</summary>
/// <remarks>
/// A contact is named by the identity the book gave it rather than by an address, because an address is a thing a person
/// has rather than a thing they are: they give one up and gain another and stay the same contact. The one command that
/// takes an address instead answers "who is this from", and it answers with a person.
/// </remarks>
internal static class ContactOptions
{
    /// <summary>Builds the option naming which contact a command acts on.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid> Identity() => new("--id")
    {
        Description = "The contact to act on, by the identifier the deployment's book gave it.",
        Required = true,
    };

    /// <summary>Builds the option naming one address a command acts on.</summary>
    /// <param name="description">What the address means for the command taking it.</param>
    /// <returns>The option.</returns>
    internal static Option<string> Address(string description) => new("--address")
    {
        Description = description,
        Required = true,
    };

    /// <summary>Builds the option naming an organization whose book a command acts on instead of a user's.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid?> Organization() => new("--organization")
    {
        Description =
            "Act on this organization's contact book, which every member reads and only an administrator writes, "
            + "instead of a user's. Cannot be combined with --user.",
    };

    /// <summary>Resolves the book a command acts on: the organization's where one was named, and otherwise what one user reads.</summary>
    /// <param name="deployment">The deployment, asked for its roster where no user was named.</param>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="requestedUser">The user the invocation named, if any.</param>
    /// <param name="requestedOrganization">The organization the invocation named, if any.</param>
    /// <param name="cancellationToken">Cancels the roster read.</param>
    /// <returns>The book.</returns>
    /// <exception cref="CliFailure">Thrown when both a user and an organization were named, or no user can be settled.</exception>
    internal static async Task<ContactBookTarget> ResolveBookAsync(
        AdminApiClient deployment,
        string token,
        Guid? requestedUser,
        Guid? requestedOrganization,
        CancellationToken cancellationToken)
    {
        if (requestedOrganization is not { } organization)
        {
            return ContactBookTarget.ForUser(
                await UserOptions.ResolveUserAsync(deployment, token, requestedUser, cancellationToken));
        }

        if (requestedUser is not null)
        {
            throw new CliFailure(
                "A contact command acts on one user's books or on one organization's book, so pass --user or --organization, not both.");
        }

        return ContactBookTarget.ForOrganization(organization);
    }
}
