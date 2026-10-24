// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Notifications.Configurations;

/// <summary>Declares which messages a merged arrival notification stands for, so a question can be asked about them.</summary>
/// <remarks>
/// A row is a pair of identifiers and nothing else. Both sides cascade, for the two obligations the notification itself
/// answers: the notification taking its pairs with it when it is read away, merged again, aged out, or erased with its
/// user, and a stored message taking the pairs that name it, so a merged notification never names mail that is gone.
/// The count the notification states stays what it was, because it says how much arrived rather than how much is left.
/// </remarks>
internal sealed class NotificationNamedEmailConfiguration : IEntityTypeConfiguration<NotificationNamedEmailEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationNamedEmailEntity> entity)
    {
        entity.ToTable("notification_named_emails");
        entity.HasKey(named => new { named.NotificationId, named.StoredEmailId });

        entity.HasOne<NotificationEntity>()
            .WithMany()
            .HasForeignKey(named => named.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexed on its own because a message's erasure reaches this table by the message alone, and the key above
        // leads with the notification.
        entity.HasOne<StoredEmailEntity>()
            .WithMany()
            .HasForeignKey(named => named.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(named => named.StoredEmailId)
            .HasDatabaseName(PersistenceConstraintNames.NotificationNamedEmailStoredEmailIndexName);
    }
}
