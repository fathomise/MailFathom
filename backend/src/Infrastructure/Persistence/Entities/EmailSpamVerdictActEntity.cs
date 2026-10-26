// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Spam;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One thing a spam verdict of this deployment did to one stored email.</summary>
/// <remarks>
/// A table of its own rather than a column on the classification, because it has to outlive the classification: a
/// reclassification replaces the verdict, and what the earlier one did to the mailbox is still what the next one must
/// neither count as evidence nor repeat.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailSpamVerdictActEntity
{
    public Guid StoredEmailId { get; set; }

    public SpamVerdictAct Act { get; set; }

    /// <summary>Gets or sets when the act was committed, which is what an operator reading the table needs to place it.</summary>
    public DateTimeOffset RecordedAt { get; set; }
}
