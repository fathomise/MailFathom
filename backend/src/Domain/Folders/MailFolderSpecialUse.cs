// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Names the role a folder plays for its account, independently of what its server calls the folder.</summary>
/// <remarks>
/// <para>
/// The roles mirror the special-use attributes of RFC 6154 and the inbox RFC 3501 mandates. Mapping an alias onto one of
/// those is what lets an account whose server names its folders in another language synchronize without an operator
/// writing a server-specific path into configuration.
/// </para>
/// <para>
/// The value <c>9</c> is retired and is never given to another role. It named an outbox an operator mapped onto a
/// folder of their choosing, which
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0040-each-account-outbox-as-a-read-only-list-beside-its-folders.md">ADR 0040</see>
/// replaced with each account's own outbox beside its folders — a stored setting or an older document may still
/// carry the number, and reusing it would give that setting a meaning nobody wrote.
/// </para>
/// </remarks>
public enum MailFolderSpecialUse
{
    /// <summary>The mailbox every IMAP server exposes for incoming mail.</summary>
    Inbox = 0,

    /// <summary>The folder holding messages the user archived.</summary>
    Archive = 1,

    /// <summary>The folder holding unsent drafts.</summary>
    Drafts = 2,

    /// <summary>The folder holding sent messages.</summary>
    Sent = 3,

    /// <summary>The folder holding messages classified as spam.</summary>
    Junk = 4,

    /// <summary>The folder holding deleted messages.</summary>
    Trash = 5,

    /// <summary>The virtual folder presenting every message in the account.</summary>
    All = 6,

    /// <summary>The virtual folder presenting flagged messages.</summary>
    Flagged = 7,

    /// <summary>The virtual folder presenting messages the server considers important.</summary>
    Important = 8,
}
