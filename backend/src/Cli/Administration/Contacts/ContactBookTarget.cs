// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>Names the contact book a command acts on: what one user reads, or one organization's.</summary>
/// <remarks>
/// A user's books are reached under the deployment's contact paths with the user in the query, because what an act
/// reaches there is what that user reads. An organization's book is reached beneath the organization's own path, which
/// is the only place it is written: what every member's mailbox recognizes is an administrative act on the
/// organization rather than on any one member.
/// </remarks>
internal sealed record ContactBookTarget
{
    private ContactBookTarget(Guid? user, Guid? organization)
    {
        this.User = user;
        this.Organization = organization;
    }

    /// <summary>Gets the user whose books are reached, or <see langword="null" /> for an organization's book.</summary>
    internal Guid? User { get; }

    /// <summary>Gets the organization whose book is reached, or <see langword="null" /> for a user's books.</summary>
    internal Guid? Organization { get; }

    /// <summary>Gets the path the book is listed and written to.</summary>
    internal string ContactsPath => this.Organization is { } organization
        ? AdminEndpointRoutes.OrganizationContactsPath(organization)
        : AdminEndpointRoutes.ContactsPath;

    /// <summary>Names what one user reads: their own book, their organization's, and their mailboxes' collected ones.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The target.</returns>
    internal static ContactBookTarget ForUser(Guid user) => new(user, organization: null);

    /// <summary>Names one organization's book.</summary>
    /// <param name="organization">The organization.</param>
    /// <returns>The target.</returns>
    internal static ContactBookTarget ForOrganization(Guid organization) => new(user: null, organization);

    /// <summary>Gets the path one contact of the book is read, amended, and erased at.</summary>
    /// <param name="contactId">The contact the path names.</param>
    /// <returns>The path, with the identity written the way a deployment's route constraint reads one.</returns>
    internal string ContactPath(Guid contactId) => $"{this.ContactsPath}/{contactId:D}";

    /// <summary>Gets the path the contact of the book holding one address is read from.</summary>
    internal string ContactByAddressPath => $"{this.ContactsPath}/by-address";

    /// <summary>Gets the path a collected contact is promoted into the book at.</summary>
    /// <param name="contactId">The collected contact the path names.</param>
    /// <returns>The path.</returns>
    internal string ContactPromotionPath(Guid contactId) => $"{this.ContactPath(contactId)}/promotion";

    /// <summary>Gets the path everything the book holds about one person is exported from.</summary>
    /// <param name="contactId">The contact the path names.</param>
    /// <returns>The path.</returns>
    internal string ContactExportPath(Guid contactId) => $"{this.ContactPath(contactId)}/export";

    /// <summary>Opens the query string of a request with the user it acts for, where it acts for one.</summary>
    /// <returns>The query string, empty for an organization's book.</returns>
    internal AdminQueryString Query() =>
        this.User is { } user ? new AdminQueryString().Add("user", user) : new AdminQueryString();
}
