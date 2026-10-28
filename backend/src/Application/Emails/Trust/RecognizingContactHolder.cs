// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Trust;

/// <summary>Whose book holds the personal contact that recognized a message's author, as the person reading it is told.</summary>
/// <remarks>
/// The two are all a reader learns. A verdict is stored once per message rather than once per reader, so the users of a
/// shared mailbox share its marks, and what one of them is shown about a contact another of them wrote is that it exists:
/// not its name, its note, or which user holds it.
/// </remarks>
public enum RecognizingContactHolder
{
    /// <summary>The reader's own book holds the contact.</summary>
    Reader = 0,

    /// <summary>The book of another user of the mailbox holds it.</summary>
    AnotherUser = 1,
}
