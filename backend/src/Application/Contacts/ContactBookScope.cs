// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Contacts;

/// <summary>The books one act reads: a user's own, their organization's, and the collected book of every mail account they are assigned — or one organization's book, for an act on it.</summary>
/// <remarks>
/// <para>
/// A read of "the contact book" is a read of several, because what a person knows about their correspondents is partly
/// what they wrote down, partly what their organization wrote down for every member, and partly what their mailboxes
/// picked up. This states which books those are and, just as importantly, in what order — because two books may hold
/// one address and only one of the two records is shown.
/// </para>
/// <para>
/// <b>The order is the user's own book first, then their organization's, then the accounts by the ordinal order of
/// their identifiers.</b> A record the user wrote therefore wins over anything else holding the same address, which is
/// the point of writing one down: their name and their note are what they see. The organization's record wins over
/// what a mailbox collected, because somebody wrote it down for them. An address two of their mailboxes both collected
/// is answered from the first of the two in that order, so the answer is the same on every read rather than whichever
/// row the database returned first — and it is the same for two users of one mailbox, because the order is the
/// accounts' own rather than the order either user's record happened to declare them in.
/// </para>
/// <para>
/// The organization's book is read here and never written through a user's scope: it is an administrative act what
/// every member's mailbox trusts, so it is written through a scope naming the organization itself, which
/// <see cref="OfOrganization(Guid, IReadOnlyList{MailAccountId})" /> composes and in which that book is
/// <see cref="OwnBook" />.
/// </para>
/// <para>
/// The order is carried as <see cref="Keys" /> rather than recomputed per read, because the store answers the
/// precedence question with it: a record is hidden when one of its addresses is held in a book that comes before its
/// own.
/// </para>
/// </remarks>
public sealed record ContactBookScope
{
    private ContactBookScope(
        ContactBookHolder ownBook,
        ContactBookHolder? organizationBook,
        IEnumerable<MailAccountId> accounts)
    {
        this.OwnBook = ownBook;
        this.OrganizationBook = organizationBook;
        this.Books =
        [
            ownBook,
            .. organizationBook is null ? [] : new[] { organizationBook },
            .. accounts
                .Select(ContactBookHolder.Of)
                .DistinctBy(book => book.Key, StringComparer.Ordinal)
                .OrderBy(book => book.Key, StringComparer.Ordinal),
        ];
        this.Keys = [.. this.Books.Select(book => book.Key)];
    }

    /// <summary>Gets the user whose reading this scope describes.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the scope is an organization's, which is read for no user.</exception>
    public UserId User => this.OwnBook.User
        ?? throw new InvalidOperationException("An organization's contact book is read for no one user.");

    /// <summary>Gets the book every write a caller makes goes into: the user's own, or the organization's when the scope names one.</summary>
    public ContactBookHolder OwnBook { get; }

    /// <summary>Gets the organization's book a user reads beside their own, or <see langword="null" /> where they belong to none or the scope is the organization's own.</summary>
    public ContactBookHolder? OrganizationBook { get; }

    /// <summary>Gets every book this scope reads, in precedence order.</summary>
    public IReadOnlyList<ContactBookHolder> Books { get; }

    /// <summary>Gets the keys of <see cref="Books" />, in the same order, which is how the store applies the precedence.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>Gets the mail accounts whose collected books this scope reads, in precedence order.</summary>
    public IReadOnlyList<MailAccountId> Accounts => [.. this.Books.Select(book => book.Account).OfType<MailAccountId>()];

    /// <summary>Describes a user reading their own book and the collected books of the accounts they are assigned, outside any organization.</summary>
    /// <param name="user">The user.</param>
    /// <param name="assignedAccounts">The accounts assigned to them, in any order.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assignedAccounts" /> is <see langword="null" />.</exception>
    public static ContactBookScope Of(UserId user, IReadOnlyList<MailAccountId> assignedAccounts) =>
        Of(user, organization: null, assignedAccounts);

    /// <summary>Describes a user reading their own book, their organization's, and the collected books of the accounts they are assigned.</summary>
    /// <param name="user">The user.</param>
    /// <param name="organization">The organization the user is a member of, or <see langword="null" /> for none.</param>
    /// <param name="assignedAccounts">The accounts assigned to them, in any order.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assignedAccounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An account named twice contributes one book, because a book is the account's rather than the assignment's, and
    /// the accounts are ordered here rather than taken as given so that the precedence between two of them does not
    /// depend on which reader assembled the list. A user assigned nothing reads their own book and their
    /// organization's, which is the same shape rather than a case of its own.
    /// </remarks>
    public static ContactBookScope Of(UserId user, Guid? organization, IReadOnlyList<MailAccountId> assignedAccounts)
    {
        ArgumentNullException.ThrowIfNull(assignedAccounts);

        return new ContactBookScope(
            ContactBookHolder.Of(user),
            organization is { } member && member != Guid.Empty ? ContactBookHolder.OfOrganization(member) : null,
            assignedAccounts);
    }

    /// <summary>Describes a read of one user's own book alone, reaching nothing any organization or mailbox holds.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The scope.</returns>
    /// <remarks>
    /// The shape a user in no organization and assigned no mail account reads in, which is what the tests stating that
    /// case are written against. Nothing in production narrows to it: every read and every write resolves the whole
    /// scope, because reaching a collected record is what lets it be refused as collected rather than reported absent.
    /// </remarks>
    public static ContactBookScope OfOwnBookAlone(UserId user) => Of(user, []);

    /// <summary>Describes an act on one organization's book, reaching the collected books of the accounts named beside it.</summary>
    /// <param name="organization">The organization whose book the act reads and writes.</param>
    /// <param name="collectedAccounts">The accounts of that organization whose collected books a promotion may copy out of, empty for every other act.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="collectedAccounts" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="organization" /> is the empty UUID.</exception>
    public static ContactBookScope OfOrganization(Guid organization, IReadOnlyList<MailAccountId> collectedAccounts)
    {
        ArgumentNullException.ThrowIfNull(collectedAccounts);

        return new ContactBookScope(ContactBookHolder.OfOrganization(organization), organizationBook: null, collectedAccounts);
    }

    /// <summary>Keeps this scope's own and organization's books, reaching the collected books of the given accounts instead of its own.</summary>
    /// <param name="accounts">The accounts whose collected books the narrowed scope reads.</param>
    /// <returns>The narrowed scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    public ContactBookScope Covering(IReadOnlyList<MailAccountId> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return new ContactBookScope(this.OwnBook, this.OrganizationBook, accounts);
    }

    /// <summary>Leaves the organization's book out of this scope, which is what a write through a user's scope reaches.</summary>
    /// <returns>The scope without the organization's book.</returns>
    /// <remarks>
    /// A member reads the organization's book and never writes it: amending, promoting, or erasing a contact found only
    /// there is answered as a contact this scope does not hold, because the organization's administrators are the only
    /// writers of that book.
    /// </remarks>
    public ContactBookScope WithoutOrganizationBook() =>
        new(this.OwnBook, organizationBook: null, this.Accounts);
}
