// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery.Receipts;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>Persists what one kind of report said about one recipient of the account's own outgoing email.</summary>
/// <remarks>
/// It belongs to the recipient row, and through it to the outgoing email: erasing the send or its account erases every
/// outcome with it. An outcome this deployment observed between two accounts of one organization also belongs to the
/// received message it was observed on, and is erased with that message and its account. A read time says when a named
/// person read the message, so it is personal data carrying the classification of the message it describes.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class OutgoingEmailReceiptOutcomeEntity
{
    /// <summary>Gets or sets the outgoing email the recipient belongs to.</summary>
    public Guid OutgoingEmailId { get; set; }

    /// <summary>Gets or sets the recipient's position in the outgoing email's recipient list.</summary>
    public int RecipientOrdinal { get; set; }

    /// <summary>Gets or sets which kind of report the outcome came from, which is what lets a recipient hold one of each.</summary>
    public ReceiptKind Kind { get; set; }

    /// <summary>Gets or sets the recipient row the outcome is about.</summary>
    public OutgoingEmailRecipientEntity Recipient { get; set; } = null!;

    /// <summary>Gets or sets what the report said.</summary>
    public ReceiptOutcome Outcome { get; set; }

    /// <summary>Gets or sets when the report says it was written.</summary>
    public DateTimeOffset ReportedAt { get; set; }

    /// <summary>Gets or sets where the outcome came from.</summary>
    public ReceiptOutcomeSource Source { get; set; }

    /// <summary>Gets or sets the RFC 3463 status code a delivery report gave, or <see langword="null" /> where it gave none.</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the server a delivery report names as having reported it, or <see langword="null" /> where it named none.</summary>
    public string? ReportingServer { get; set; }

    /// <summary>Gets or sets the recipient's stored copy the outcome was observed on, set only where <see cref="Source" /> is <see cref="ReceiptOutcomeSource.Organization" />.</summary>
    public Guid? ObservedInStoredEmailId { get; set; }
}
