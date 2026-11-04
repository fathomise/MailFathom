// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>What one caller's flag and keyword change became: a durable record per value it asked for.</summary>
/// <param name="StoredEmailId">The email the change was asked for.</param>
/// <param name="AccountId">The account that email belongs to, which is the account whose run will carry the change.</param>
/// <param name="Folder">The folder the email is in.</param>
/// <param name="Recorded">One entry per value the request named that a record was opened for, in the order the request states them, and empty where none was.</param>
/// <param name="IsApplied">Whether every value was committed to stored state as the call committed.</param>
/// <remarks>
/// On an account whose source is the truth it reports what was written down rather than what a mail server has done,
/// because at the moment this is produced no command has gone out. That is the honest answer and it is also the useful
/// one: the record is what survives a crash, what convergence resumes, and what an operator reads when a change has not
/// arrived, so its identity is what a caller needs in hand. On a held account the change has already happened when this
/// is produced, and there is no record to name. The two are not exclusive: a mirrored account that commits a change
/// locally first, and an account being restored to its source, have both applied the change and opened the record the
/// server is still owed, and report each.
/// </remarks>
public sealed record AuthoredMailFlagChangeResult(
    StoredEmailId StoredEmailId,
    MailAccountId AccountId,
    MailFolderId Folder,
    IReadOnlyList<RecordedMailFlagMutation> Recorded,
    bool IsApplied = false);

/// <summary>One value a change asked for, and the durable record that now carries it.</summary>
/// <param name="Mutation">The change that was written down, under the name every log line and counter uses for it.</param>
/// <param name="RecordId">The record everything afterwards refers to that change by.</param>
/// <param name="Lifecycle">Where that record stands, which is pending for a change nothing has attempted yet.</param>
/// <remarks>
/// The lifecycle is reported rather than assumed to be pending, because a request repeated under the identity that
/// already produced one is answered with the record it produced. A caller retrying a call therefore learns that its
/// change has already been made instead of being told a second one was opened.
/// </remarks>
public sealed record RecordedMailFlagMutation(
    MailboxMutation Mutation,
    MailboxMutationRecordId RecordId,
    MailboxMutationLifecycle Lifecycle)
{
    /// <summary>Describes a record a mutation was written down as.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The mutation, the record's identity, and the lifecycle it stands in.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    public static RecordedMailFlagMutation Of(MailboxMutationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new RecordedMailFlagMutation(record.Request.Mutation, record.Id, record.Lifecycle);
    }
}
