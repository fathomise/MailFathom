// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Linq.Expressions;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Tells mail the mailbox has not sent — a draft, or a send waiting in or refused by the outbox — from mail that left or arrived.</summary>
/// <remarks>
/// <para>
/// A send waiting to leave is filed into the folder the account maps as drafts with <c>\Draft</c>, beside the drafts the
/// person is still writing, so both are recognized the same way: the message carries <c>\Draft</c>, or sits in that
/// folder. Neither is a word anybody received, which is why no derived work runs over it and no agent but the person's own
/// Agent reads it.
/// </para>
/// <para>
/// <c>MailboxScopeResolver</c> states the same rule about one email a read reaches by its identifier, and the two change
/// together.
/// </para>
/// </remarks>
internal static class UnsentStoredEmails
{
    /// <summary>Selects the mail the mailbox has not sent.</summary>
    internal static readonly Expression<Func<StoredEmailEntity, bool>> IsUnsent =
        email => email.IsDraft || email.Folder.Role == MailFolderSpecialUse.Drafts;

    /// <summary>Selects the mail that left the mailbox or arrived in it.</summary>
    internal static readonly Expression<Func<StoredEmailEntity, bool>> IsSentOrReceived =
        email => !email.IsDraft && email.Folder.Role != MailFolderSpecialUse.Drafts;

    /// <summary>Reports whether one stored email, held in memory with the folder it sits in, is mail the mailbox has not sent.</summary>
    /// <param name="email">The email.</param>
    /// <param name="folder">The folder it sits in, or <see langword="null" /> where none was found.</param>
    /// <returns><see langword="true" /> when the email carries <c>\Draft</c> or sits in the folder its account maps as drafts.</returns>
    internal static bool Holds(StoredEmailEntity email, MailFolderEntity? folder) =>
        email.IsDraft || folder?.Role == MailFolderSpecialUse.Drafts;
}
