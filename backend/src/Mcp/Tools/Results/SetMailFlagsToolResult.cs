// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Application.Mail.Mutations.Authoring;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what a request to write flags and keywords was written down as.</summary>
/// <remarks>
/// <para>
/// It answers with what MailFathom's copy of the email shows and with the records still owed to the mail server, because
/// at the moment it is produced no IMAP command has gone out. <c>applied</c> says the copy already shows the change — on a
/// held account, on a restoring one, and on a mirrored one that committed it locally first — and whenever records stand
/// beside it they are what carries the change to the server: the account's own run issues each one, and a change that
/// never arrives is found by its record. A held account has no server, so it publishes none.
/// </para>
/// <para>
/// Nothing derived from the message appears. The email's own identifier, the account, the folder alias, and MailFathom's
/// record identities are all its own names for things, and the keywords the caller sent are not repeated back — a label
/// is text the user chose and can name a person or a case, and the caller already holds what it wrote.
/// </para>
/// </remarks>
[Description("What the change was written down as: whether MailFathom's copy of the email already shows it, and one durable record per value asked for, each carried to the mail server by the account's next run — none on a held account.")]
internal sealed record SetMailFlagsToolResult
{
    /// <summary>Gets the email the change was written down against.</summary>
    [Description("The storedEmailId the change was recorded against, which is the one the call named.")]
    public required string StoredEmailId { get; init; }

    /// <summary>Gets the account whose run will carry the change.</summary>
    [Description("The account the email belongs to. Its next synchronization run is what carries each of the recordedChanges to the mail server, including those beside a change already applied; a held account has none to carry.")]
    public required string AccountId { get; init; }

    /// <summary>Gets the identity of the folder the email is in.</summary>
    [Description("The identity of the folder the email is in, the folderId a listed email and list_accounts report it by.")]
    public required string FolderId { get; init; }

    /// <summary>Gets whether the change was committed to MailFathom's copy of the email as the call committed.</summary>
    [Description("True when MailFathom's copy of the email already shows the change. Any recordedChanges are then what carries it to the mail server; on a held account, where MailFathom alone keeps the mail, there are none. False when the change was only written down, and the email shows it once the mail server has taken it.")]
    public required bool Applied { get; init; }

    /// <summary>Gets one entry per value the call asked for.</summary>
    [Description("One entry per value asked for, in the order seen, flagged, keywords. A call that asked for one value carries one entry, and a call on a held account carries none.")]
    public required IReadOnlyList<RecordedMailboxChange> RecordedChanges { get; init; }

    /// <summary>Publishes what the use case recorded.</summary>
    /// <param name="result">What was written down.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    public static SetMailFlagsToolResult From(AuthoredMailFlagChangeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new SetMailFlagsToolResult
        {
            StoredEmailId = result.StoredEmailId.ToString(),
            AccountId = result.AccountId.Value,
            FolderId = result.Folder.ToString(),
            Applied = result.IsApplied,
            RecordedChanges =
            [
                .. result.Recorded.Select(static recorded => new RecordedMailboxChange
                {
                    Change = recorded.Mutation.Name,
                    ChangeRecordId = recorded.RecordId.ToString(),
                    State = recorded.Lifecycle.Name,
                }),
            ],
        };
    }
}
