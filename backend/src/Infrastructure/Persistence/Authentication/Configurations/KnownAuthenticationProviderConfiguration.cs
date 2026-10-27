// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Authentication.Configurations;

/// <summary>Declares the known authentication providers and the rule that one host has at most one entry.</summary>
internal sealed class KnownAuthenticationProviderConfiguration : IEntityTypeConfiguration<KnownAuthenticationProviderEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<KnownAuthenticationProviderEntity> entity)
    {
        entity.ToTable(KnownAuthenticationProviderEntity.TableName);
        entity.HasKey(provider => provider.Id);
        entity.Property(provider => provider.Id).ValueGeneratedNever();
        entity.Property(provider => provider.ImapHost)
            .HasMaxLength(KnownAuthenticationProviderEntity.MaximumValueLength)
            .IsRequired();
        entity.Property(provider => provider.AuthorityIdentifier)
            .HasMaxLength(KnownAuthenticationProviderEntity.MaximumValueLength)
            .IsRequired();
        entity.Property(provider => provider.AddedBy)
            .HasMaxLength(KnownAuthenticationProviderEntity.MaximumChangedByLength)
            .IsRequired();

        // One entry per host, enforced here rather than by a read before the write: two replicas adding the same host
        // at once leave one entry, and the loser's insert does nothing.
        entity.HasIndex(provider => provider.ImapHost)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.KnownAuthenticationProviderHostUniqueIndexName);
    }
}
