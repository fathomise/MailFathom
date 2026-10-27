// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Spam.Configurations;

/// <summary>Declares the record of what this deployment's spam verdicts did to each stored email.</summary>
/// <remarks>
/// The key is the email and the act, so an act is recorded once however often it is asked for, and the table cascades
/// from the email so the record is erased with the mail it describes.
/// </remarks>
internal sealed class EmailSpamVerdictActConfiguration : IEntityTypeConfiguration<EmailSpamVerdictActEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailSpamVerdictActEntity> entity)
    {
        entity.ToTable("email_spam_verdict_acts");
        entity.HasKey(act => new { act.StoredEmailId, act.Act })
            .HasName(PersistenceConstraintNames.EmailSpamVerdictActPrimaryKeyConstraintName);
        entity.Property(act => act.Act).HasConversion<string>().HasMaxLength(64);

        entity.HasOne<StoredEmailEntity>()
            .WithMany()
            .HasForeignKey(act => act.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
