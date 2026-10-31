// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.AutomaticReplies;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.AutomaticReplies.Configurations;

internal sealed class AutomaticReplyConfiguration : IEntityTypeConfiguration<AutomaticReplyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AutomaticReplyEntity> entity)
    {
        entity.ToTable(AutomaticReplyEntity.TableName);
        entity.HasKey(reply => reply.UserId);
        entity.Property(reply => reply.UserId).ValueGeneratedNever();
        entity.Property(reply => reply.InsideText).HasMaxLength(AutomaticReply.MaximumTextLength);
        entity.Property(reply => reply.OutsideText).HasMaxLength(AutomaticReply.MaximumTextLength).IsRequired();
        entity.Property(reply => reply.AccountIds).IsRequired();

        // Cascade rather than a statement in the erasure walk, for the reason the client preferences key the same way:
        // the reply is the user's own writing about themselves, so it goes when they do.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(reply => reply.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
