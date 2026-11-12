// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Files;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Agent.Configurations;

/// <summary>Declares the row one conversation's file is recorded by.</summary>
internal sealed class AgentConversationFileConfiguration : IEntityTypeConfiguration<AgentConversationFileEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AgentConversationFileEntity> entity)
    {
        entity.ToTable(AgentConversationFileEntity.TableName);
        entity.HasKey(file => file.Id).HasName(PersistenceConstraintNames.AgentConversationFilePrimaryKeyConstraintName);
        entity.Property(file => file.Id).ValueGeneratedNever();
        entity.Property(file => file.Name).HasMaxLength(AgentConversationFileBounds.MaximumNameLength);
        entity.Property(file => file.Kind).HasMaxLength(32);
        entity.Property(file => file.MediaType).HasMaxLength(AgentConversationFileEntity.MaximumMediaTypeLength);
        entity.Property(file => file.TextOutcome).HasMaxLength(64);
        entity.Property(file => file.PageKind).HasMaxLength(32);
        entity.Property(file => file.Segments).HasColumnType("jsonb");

        // The stored file rather than a statement of the deletion: a file whose octets are gone has nothing left to be.
        entity.HasOne<StoredFileEntity>()
            .WithOne()
            .HasForeignKey<AgentConversationFileEntity>(file => file.Id)
            .HasConstraintName(PersistenceConstraintNames.AgentConversationFileStoredFileForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // The conversation, so deleting one is still the single statement it was and takes every file row with it.
        entity.HasOne<AgentConversationEntity>()
            .WithMany()
            .HasForeignKey(file => file.ConversationId)
            .HasConstraintName(PersistenceConstraintNames.AgentConversationFileConversationForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(file => new { file.ConversationId, file.AddedAt })
            .HasDatabaseName(PersistenceConstraintNames.AgentConversationFileConversationIndexName);
    }
}
