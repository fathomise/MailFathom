// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one job at a stored email whose junk marks another mail client changed, and at nothing inside the message.</summary>
/// <remarks>Which marks changed is deliberately absent: a label is personal metadata, and the handler reads the marks the row carries now.</remarks>
public sealed record FollowJunkMarksJobPayload : IJobPayload
{
    /// <summary>Gets the account whose mailbox the email belongs to.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets the stored identity of the email to decide again.</summary>
    public required Guid EmailRecordId { get; init; }

    /// <summary>Gets when synchronization observed the change, which a person's verdict is held against.</summary>
    public required DateTimeOffset ChangeObservedAt { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.FollowJunkMarks;

    /// <summary>Describes one observed change as the document a job carries.</summary>
    /// <param name="account">The account the email belongs to.</param>
    /// <param name="email">The email's stored identity.</param>
    /// <param name="changeObservedAt">When synchronization observed the change.</param>
    /// <returns>The payload naming that change.</returns>
    public static FollowJunkMarksJobPayload For(
        MailAccountId account,
        StoredEmailId email,
        DateTimeOffset changeObservedAt)
    {
        return new FollowJunkMarksJobPayload
        {
            AccountId = account.Value,
            EmailRecordId = email.Value,
            ChangeObservedAt = changeObservedAt,
        };
    }

    /// <summary>Rebuilds the generated account identifier this payload names.</summary>
    /// <returns>The account's generated identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored value no longer names a valid account.</exception>
    public MailAccountId ToAccountId() => MailAccountId.Create(this.AccountId);

    /// <summary>Rebuilds the stored identity this payload names.</summary>
    /// <returns>The email's stored identity.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored value is the empty identifier.</exception>
    public StoredEmailId ToStoredEmailId() => StoredEmailId.Create(this.EmailRecordId);
}
