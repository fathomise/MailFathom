// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Rules.Configurations;

/// <summary>Maps the row one account's rules hang from, and the cascade that takes them with the account.</summary>
internal sealed class MailRuleSetConfiguration : IEntityTypeConfiguration<MailRuleSetEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailRuleSetEntity> entity)
    {
        entity.ToTable("mail_rule_sets");
        entity.HasKey(set => set.MailboxAccountId);
        entity.Property(set => set.MailboxAccountId).HasMaxLength(128).ValueGeneratedNever();

        // The account's own cascade is the whole of the erasure seam for rules: an erased account takes its set, its
        // rules, and its archive with it, with nothing to remember.
        entity.HasOne<MailboxAccountEntity>()
            .WithMany()
            .HasForeignKey(set => set.MailboxAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
