// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Authentication.Configurations;

/// <summary>Declares the append-only record of every change to the known authentication providers.</summary>
/// <remarks>
/// The row hangs on nothing: it carries the entry's values rather than a reference to the entry, so removing the entry
/// leaves its record behind. Nothing here is mail content — a host and an authserv-id are a provider's own names.
/// </remarks>
internal sealed class KnownAuthenticationProviderChangeConfiguration : IEntityTypeConfiguration<KnownAuthenticationProviderChangeEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<KnownAuthenticationProviderChangeEntity> entity)
    {
        entity.ToTable(
            KnownAuthenticationProviderChangeEntity.TableName,
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.KnownAuthenticationProviderChangeActCheckConstraintName,
                $"\"Act\" IN ('{KnownAuthenticationProviderChangeEntity.Added}', '{KnownAuthenticationProviderChangeEntity.Removed}')"));
        entity.HasKey(change => change.Id);
        entity.Property(change => change.Id).ValueGeneratedNever();
        entity.Property(change => change.ImapHost)
            .HasMaxLength(KnownAuthenticationProviderEntity.MaximumValueLength)
            .IsRequired();
        entity.Property(change => change.AuthorityIdentifier)
            .HasMaxLength(KnownAuthenticationProviderEntity.MaximumValueLength)
            .IsRequired();
        entity.Property(change => change.Act).HasMaxLength(16).IsRequired();
        entity.Property(change => change.ChangedBy)
            .HasMaxLength(KnownAuthenticationProviderEntity.MaximumChangedByLength)
            .IsRequired();
    }
}
