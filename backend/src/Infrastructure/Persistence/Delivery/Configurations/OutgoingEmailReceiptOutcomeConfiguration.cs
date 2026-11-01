// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares what the reports that came back said about each recipient of an outgoing email.</summary>
/// <remarks>
/// Keyed by the recipient row and the kind of report rather than by an address, for the reason the recipient table is:
/// an address is personal data and a key is repeated into every index over the table. It also means a report can only
/// ever mark somebody the message was actually sent to.
/// </remarks>
internal sealed class OutgoingEmailReceiptOutcomeConfiguration : IEntityTypeConfiguration<OutgoingEmailReceiptOutcomeEntity>
{
    /// <summary>The longest status code kept, which is longer than any RFC 3463 code.</summary>
    internal const int MaximumStatusLength = 16;

    /// <summary>The longest server name kept, which is the longest a domain name can be.</summary>
    internal const int MaximumReportingServerLength = 255;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutgoingEmailReceiptOutcomeEntity> entity)
    {
        entity.ToTable("outgoing_email_receipt_outcomes");
        entity.HasKey(outcome => new { outcome.OutgoingEmailId, outcome.RecipientOrdinal, outcome.Kind })
            .HasName(PersistenceConstraintNames.OutgoingEmailReceiptOutcomePrimaryKeyConstraintName);

        // Stored as text for the reason every other enum on the outgoing side is.
        entity.Property(outcome => outcome.Kind).HasConversion<string>().HasMaxLength(64);
        entity.Property(outcome => outcome.Outcome).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(outcome => outcome.Source).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(outcome => outcome.Status).HasMaxLength(MaximumStatusLength);
        entity.Property(outcome => outcome.ReportingServer).HasMaxLength(MaximumReportingServerLength);

        entity.HasOne(outcome => outcome.Recipient)
            .WithMany()
            .HasForeignKey(outcome => new { outcome.OutgoingEmailId, outcome.RecipientOrdinal })
            .HasConstraintName(PersistenceConstraintNames.OutgoingEmailReceiptOutcomeRecipientForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // The recipient's own copy is what an organization's delivery and read were observed on, so erasing that copy —
        // or the receiving account with it — erases what was reported about it, whatever happens to the send.
        entity.HasOne<StoredEmailEntity>()
            .WithMany()
            .HasForeignKey(outcome => outcome.ObservedInStoredEmailId)
            .HasConstraintName(PersistenceConstraintNames.OutgoingEmailReceiptOutcomeObservedInForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
