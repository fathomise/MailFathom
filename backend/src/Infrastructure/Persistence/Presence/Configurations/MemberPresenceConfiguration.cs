// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Presence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Presence.Configurations;

/// <summary>Declares the one row per member holding their chosen presence, when they were last seen, their status message, and their work mode.</summary>
/// <remarks>The member is the key and the foreign key at once, so the row goes with the user and there is never a second one.</remarks>
internal sealed class MemberPresenceConfiguration : IEntityTypeConfiguration<MemberPresenceEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MemberPresenceEntity> entity)
    {
        entity.ToTable(MemberPresenceEntity.TableName);
        entity.HasKey(presence => presence.UserId);
        entity.Property(presence => presence.UserId)
            .HasColumnName(MemberPresenceEntity.UserIdColumnName)
            .ValueGeneratedNever();
        entity.Property(presence => presence.ChosenState)
            .HasColumnName(MemberPresenceEntity.ChosenStateColumnName);
        entity.Property(presence => presence.ChoiceEndsAt)
            .HasColumnName(MemberPresenceEntity.ChoiceEndsAtColumnName);
        entity.Property(presence => presence.LastSeenAt)
            .HasColumnName(MemberPresenceEntity.LastSeenAtColumnName);
        entity.Property(presence => presence.StatusMessage)
            .HasColumnName(MemberPresenceEntity.StatusMessageColumnName)
            .HasMaxLength(StatusMessage.MostCharacters);
        entity.Property(presence => presence.StatusMessageClears)
            .HasColumnName(MemberPresenceEntity.StatusMessageClearsColumnName);
        entity.Property(presence => presence.StatusMessageClearsAt)
            .HasColumnName(MemberPresenceEntity.StatusMessageClearsAtColumnName);
        entity.Property(presence => presence.WorkMode)
            .HasColumnName(MemberPresenceEntity.WorkModeColumnName);
        entity.Property(presence => presence.WorkModeEndsAt)
            .HasColumnName(MemberPresenceEntity.WorkModeEndsAtColumnName);
        entity.HasOne<UserAccountEntity>()
            .WithOne()
            .HasForeignKey<MemberPresenceEntity>(presence => presence.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
