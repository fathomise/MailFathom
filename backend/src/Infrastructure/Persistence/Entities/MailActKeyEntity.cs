// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The key one person's client generated for a mailbox act, on one message, with what the first attempt answered.</summary>
/// <remarks>
/// It names the message without a foreign key to it, because the key must outlive the stored email for its whole window:
/// a repeat after the message was deleted is still owed the first answer.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailActKeyEntity
{
    /// <summary>The length of a SHA-256 digest written as hexadecimal.</summary>
    internal const int MaximumFingerprintLength = 64;

    /// <summary>Gets or sets the row's own identity, which is what the retention sweep deletes by.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the person who made the act.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the text the client generated for the act.</summary>
    public required string Key { get; set; }

    /// <summary>Gets or sets the message the row answers for.</summary>
    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets the SHA-256 digest of what the whole request asked, as lower-case hexadecimal.</summary>
    public required string Fingerprint { get; set; }

    /// <summary>Gets or sets the JSON document of what the first attempt answered for the message.</summary>
    public required string Outcome { get; set; }

    /// <summary>Gets or sets when the act was committed, which the retention counts from.</summary>
    public DateTimeOffset RecordedAt { get; set; }
}
