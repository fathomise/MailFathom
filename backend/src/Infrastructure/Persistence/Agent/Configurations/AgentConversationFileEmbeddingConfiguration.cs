// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Agent.Configurations;

/// <summary>Declares the row one passage of a conversation's file is placed in a space by.</summary>
internal sealed class AgentConversationFileEmbeddingConfiguration : IEntityTypeConfiguration<AgentConversationFileEmbeddingEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AgentConversationFileEmbeddingEntity> entity)
    {
        entity.ToTable(
            AgentConversationFileEmbeddingEntity.TableName,
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.AgentConversationFileEmbeddingDimensionCheckConstraintName,
                "vector_dims(\"Embedding\") = \"Dimension\""));

        entity.HasKey(embedding => new { embedding.FileId, embedding.Ordinal, embedding.EmbeddingProfileId })
            .HasName(PersistenceConstraintNames.AgentConversationFileEmbeddingPrimaryKeyConstraintName);
        entity.Property(embedding => embedding.Ordinal).ValueGeneratedNever();
        entity.Property(embedding => embedding.Embedding).HasColumnType("vector").IsRequired();

        entity.HasOne<AgentConversationFileEntity>()
            .WithMany()
            .HasForeignKey(embedding => embedding.FileId)
            .HasConstraintName(PersistenceConstraintNames.AgentConversationFileEmbeddingFileForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // Declared for the reason the mail vectors' index is: a superseded space's vectors are removed by profile.
        entity.HasIndex(embedding => new { embedding.EmbeddingProfileId, embedding.Dimension })
            .HasDatabaseName(PersistenceConstraintNames.AgentConversationFileEmbeddingProfileIndexName);

        entity.HasOne<EmbeddingProfileEntity>()
            .WithMany()
            .HasForeignKey(embedding => new { embedding.EmbeddingProfileId, embedding.Dimension })
            .HasPrincipalKey(profile => new { profile.Id, profile.Dimension })
            .HasConstraintName(PersistenceConstraintNames.AgentConversationFileEmbeddingProfileForeignKeyName)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
