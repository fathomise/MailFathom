// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Mutations.Configurations;

/// <summary>Declares the keys people's mailbox acts were made under.</summary>
/// <remarks>
/// <para>
/// A table of its own rather than a column of <c>mailbox_mutations</c>: a mutation record ends with its stored email and
/// a held account's local commit writes none, while a key has to answer for its whole window after either.
/// </para>
/// <para>
/// One row per person, key, and message, enforced by the database. Two replicas given the same retried request reach
/// the insert together and one loses on the unique index, which is how the loser learns the first got there and answers
/// the first outcome instead of changing the mailbox twice.
/// </para>
/// </remarks>
internal sealed class MailActKeyConfiguration : IEntityTypeConfiguration<MailActKeyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailActKeyEntity> entity)
    {
        entity.ToTable("mail_act_keys");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Key).HasMaxLength(MailboxMutationRequester.MaximumIdentityLength).IsRequired();
        entity.Property(row => row.Fingerprint).HasMaxLength(MailActKeyEntity.MaximumFingerprintLength).IsRequired();
        entity.Property(row => row.Outcome).HasColumnType("jsonb").IsRequired();

        // Cascades from the person, for the reason a notification does: a key says what somebody asked of their mailbox,
        // so it goes when they do, and this table names no mail account for an erasure walk to find it by. There is
        // deliberately no key onto the stored email, which would end the row with the message inside its window.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(row => row.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The identity of an act on a message, and also the read of a key by person alone: the request-level check
        // takes the first row any message of the key left, which is the leading prefix of this index.
        entity.HasIndex(row => new { row.UserId, row.Key, row.StoredEmailId })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailActKeyUniqueIndexName);

        // What the retention sweep walks: the oldest rows first, with the identity as the tie-break.
        entity.HasIndex(row => new { row.RecordedAt, row.Id })
            .HasDatabaseName(PersistenceConstraintNames.MailActKeyRecordedIndexName);
    }
}
