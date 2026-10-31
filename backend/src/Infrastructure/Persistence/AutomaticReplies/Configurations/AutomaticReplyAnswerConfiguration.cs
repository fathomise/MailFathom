// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.AutomaticReplies;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.AutomaticReplies.Configurations;

internal sealed class AutomaticReplyAnswerConfiguration : IEntityTypeConfiguration<AutomaticReplyAnswerEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AutomaticReplyAnswerEntity> entity)
    {
        entity.ToTable(AutomaticReplyAnswerEntity.TableName);

        // The key is the guarantee: one row per sender per revision, so of two replicas answering one sender at once
        // only one inserts.
        entity.HasKey(answer => new { answer.UserId, answer.Revision, answer.SenderAddress });
        entity.Property(answer => answer.SenderAddress)
            .HasMaxLength(AutomaticReplyRecipientPolicy.MaximumAddressLength)
            .IsRequired();

        // The answered senders belong to the reply that answered them and go with it, and with the user.
        entity.HasOne<AutomaticReplyEntity>()
            .WithMany()
            .HasForeignKey(answer => answer.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
