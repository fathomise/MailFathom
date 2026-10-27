// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>Names whose contact book one act reads or writes: a user's own, an organization's, or a mail account's.</summary>
/// <remarks>
/// <para>
/// There are three kinds of book and the kind decides the origin of everything in it. A user holds the people they wrote
/// down, and an organization holds the people its administrators wrote down for every member, both of which are
/// <see cref="ContactOrigin.Asserted" />; a mail account holds the people its mail says it corresponds with, which is
/// <see cref="ContactOrigin.Collected" />. So a holder is not merely a key a row is filed under — it is the whole of what
/// makes collection unable to write into a written-down book and a user unable to amend what a mailbox picked up.
/// </para>
/// <para>
/// A mailbox is one mailbox however many people are assigned it, which is what puts the collected book on the account
/// rather than on each of them: an address the account corresponds with is recorded once and every assigned user reads
/// that one record, instead of one copy per person appearing the day an administrator assigns a second. An
/// organization's book is one book for the same reason: every member reads the one record its administrators wrote.
/// </para>
/// <para>
/// <see cref="Key" /> is what the store files a row under, and it is text because the identifiers are: a user and an
/// organization are UUIDs and a mail account is the identifier the deployment generated for its record, which the mail
/// graph beside this one already records as text. It leads with the kind, and that prefix is load-bearing rather than
/// decorative: the identifiers are minted independently of each other, so nothing about one makes it unable to equal
/// another, and two that did would make, say, that user's own book and that mailbox's collected book one key — every
/// read narrows on the key alone, so every other user assigned the account would read that user's own contacts. No
/// value is checked against another's table anywhere, and no constraint could be: the prefix is what makes the
/// namespaces unable to meet, and the check constraint on the table asserts the same derivation so a row cannot be
/// filed under a key its own holder columns do not produce.
/// </para>
/// </remarks>
public sealed record ContactBookHolder
{
    /// <summary>The prefix a user's own book's key carries, which is what keeps it out of the other namespaces.</summary>
    public const string UserKeyPrefix = "user:";

    /// <summary>The prefix a mail account's collected book's key carries, which is what keeps it out of the other namespaces.</summary>
    public const string AccountKeyPrefix = "account:";

    /// <summary>The prefix an organization's book's key carries, which is what keeps it out of the other namespaces.</summary>
    public const string OrganizationKeyPrefix = "organization:";

    private ContactBookHolder(
        ContactOrigin origin,
        string key,
        UserId? user,
        MailAccountId? account,
        Guid? organization)
    {
        this.Origin = origin;
        this.Key = key;
        this.User = user;
        this.Account = account;
        this.Organization = organization;
    }

    /// <summary>Gets the origin every contact in this book carries.</summary>
    public ContactOrigin Origin { get; }

    /// <summary>Gets the value a row of this book is filed under, which leads with the kind of holder it names.</summary>
    public string Key { get; }

    /// <summary>Gets the user whose own book this is, or <see langword="null" /> when it is not a user's.</summary>
    public UserId? User { get; }

    /// <summary>Gets the mail account whose collected book this is, or <see langword="null" /> when it is not a mail account's.</summary>
    public MailAccountId? Account { get; }

    /// <summary>Gets the organization whose book this is, or <see langword="null" /> when it is not an organization's.</summary>
    public Guid? Organization { get; }

    /// <summary>Names one user's own book, which holds the people they wrote down.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The holder.</returns>
    public static ContactBookHolder Of(UserId user) =>
        new(ContactOrigin.Asserted, UserKeyPrefix + user.Value.ToString("D"), user, account: null, organization: null);

    /// <summary>Names one mail account's book, which holds the people its mail says it corresponds with.</summary>
    /// <param name="account">The mail account.</param>
    /// <returns>The holder.</returns>
    public static ContactBookHolder Of(MailAccountId account) =>
        new(ContactOrigin.Collected, AccountKeyPrefix + account.Value, user: null, account, organization: null);

    /// <summary>Names one organization's book, which holds the people its administrators wrote down for every member.</summary>
    /// <param name="organization">The organization.</param>
    /// <returns>The holder.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="organization" /> is the empty UUID, which names no organization.</exception>
    public static ContactBookHolder OfOrganization(Guid organization)
    {
        if (organization == Guid.Empty)
        {
            throw new ArgumentException("An organization's book names an organization.", nameof(organization));
        }

        return new(
            ContactOrigin.Asserted,
            OrganizationKeyPrefix + organization.ToString("D"),
            user: null,
            account: null,
            organization);
    }
}
