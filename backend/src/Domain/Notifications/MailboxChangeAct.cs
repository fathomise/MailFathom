// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Notifications;

/// <summary>What a change a notification reports was asked to do to a message, named for the person rather than for the record behind it.</summary>
/// <remarks>
/// A record names the command a mail server is told, and a person never asked for a command: setting the seen flag to
/// true is marking a message read, and the value it was set to is half of what was asked. So the act folds the command
/// and its value into one name, which is the one thing a row about a change that never happened has to say.
/// </remarks>
public enum MailboxChangeAct
{
    /// <summary>The message was to be marked read.</summary>
    MarkRead = 0,

    /// <summary>The message was to be marked unread.</summary>
    MarkUnread = 1,

    /// <summary>The message was to be flagged.</summary>
    Flag = 2,

    /// <summary>The message was to lose its flag.</summary>
    Unflag = 3,

    /// <summary>The message was to be moved to another folder, which is what archiving it is as well.</summary>
    Move = 4,

    /// <summary>The message was to be deleted.</summary>
    Delete = 5,

    /// <summary>The message was to be copied into another folder.</summary>
    Copy = 6,

    /// <summary>The message's tags were to change.</summary>
    ChangeTags = 7,
}
