// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Presence.Configurations;

/// <summary>Declares the clients members keep open.</summary>
/// <remarks>
/// The member and the client's own identifier are the key, which is also the order a member's rows are read in. The
/// one index beside it is the expiry, which the deployment-wide sweep walks. The column names are the entity's own
/// constants because every statement is composed.
/// </remarks>
internal sealed class PresenceClientConfiguration : IEntityTypeConfiguration<PresenceClientEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PresenceClientEntity> entity)
    {
        entity.ToTable(PresenceClientEntity.TableName);
        entity.HasKey(client => new { client.UserId, client.ClientId });
        entity.Property(client => client.UserId)
            .HasColumnName(PresenceClientEntity.UserIdColumnName);
        entity.Property(client => client.ClientId)
            .HasColumnName(PresenceClientEntity.ClientIdColumnName)
            .ValueGeneratedNever();
        entity.Property(client => client.Activity)
            .HasColumnName(PresenceClientEntity.ActivityColumnName);
        entity.Property(client => client.ExpiresAt)
            .HasColumnName(PresenceClientEntity.ExpiresAtColumnName);
        entity.HasIndex(client => client.ExpiresAt)
            .HasDatabaseName(PersistenceConstraintNames.PresenceClientExpiryIndexName);

        // Cascade, as the signal ticket does: a client row names one person and nobody else, so it goes when they do
        // without an erasure having to know this table exists.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(client => client.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
