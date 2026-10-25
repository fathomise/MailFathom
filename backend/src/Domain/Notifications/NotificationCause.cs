// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Notifications;

/// <summary>What a notification was raised for, as a closed set rather than as the sentence it is said in.</summary>
/// <remarks>
/// A notification's words are a rendering decision and the service takes none: it holds which condition occurred and
/// the numbers that condition is stated with, and whoever draws the row says it in the language that reader has. The
/// set is therefore the record's rather than one producer's, exactly as <see cref="NotificationKind" /> is, and a
/// condition that gains a producer joins it here.
/// </remarks>
public enum NotificationCause
{
    /// <summary>More mail arrived for the person in one account than is said a message at a time, so it is counted instead.</summary>
    MailArrived = 0,

    /// <summary>A synchronization run ended with folders it did not finish.</summary>
    SynchronizationIncomplete = 1,

    /// <summary>The mail server refused the credential MailFathom holds for an account.</summary>
    CredentialRefused = 2,

    /// <summary>A reminder somebody set on one of their own calendar events has come due.</summary>
    CalendarReminderDue = 3,

    /// <summary>A reminder somebody set against one of their own tasks' due dates has come due.</summary>
    TaskReminderDue = 4,

    /// <summary>One message arrived for the person, and the notification names its sender and its subject.</summary>
    MessageArrived = 5,

    /// <summary>The mail server refused a message handed to an account's outbox, so it waits there for somebody to decide about it.</summary>
    SendRefused = 6,

    /// <summary>A change somebody asked of their mailbox never reached the mail server, and MailFathom stopped trying to make it.</summary>
    MailboxChangeAbandoned = 7,

    /// <summary>A draft's copy in the mail server's drafts folder could not be kept there, or nobody knows whether it was.</summary>
    DraftCopyNotKept = 8,
}
