// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Notifications;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Notifications.Configurations;

/// <summary>Declares what a person is told about, and the ways a row leaves again.</summary>
/// <remarks>
/// <para>
/// The user's own row takes their notifications with them, so an erasure request never has to know this table exists.
/// Every record a notification can lead to takes the notifications leading to it as well — a stored message, a calendar
/// event, a task, a send, a change somebody asked of a mailbox, and a draft — so nothing can leave a row pointing at
/// something that is gone. That is the whole reason each is an association here where the audit trails beside this
/// table deliberately keep theirs as a value: a trail records an act and has to outlive what it acted on, while a
/// notification only offers to open something.
/// </para>
/// <para>
/// A title and a body are derived when the notification is produced. Mail content reaches this table in two shapes: an
/// arrived message's sender and subject, and the subject of a send the mail server did not take or of a draft whose
/// server copy fell out of step, carried as the body. Each of those rows leads to the record it names and cascades with
/// it. A change a mailbox did not take carries no mail at all, only the act it asked for, and every other notification
/// carries MailFathom's own sentences about a count or a condition. The cause and its two counts are that condition as a
/// name and two numbers, which is what a client says in its own language; the source is an account identifier; and the
/// deduplication key is a condition's name with, for a failure, the identifier of the record that failed. What makes
/// every row personal data is that it says something reached or left this person's mailbox and when, which is what the
/// retention bound and the cascades answer for.
/// </para>
/// </remarks>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationEntity> entity)
    {
        entity.ToTable("notifications");
        entity.HasKey(notification => notification.Id);
        entity.Property(notification => notification.Id).ValueGeneratedNever();

        // Stored as text for the reason every other enum in this model is: all three stay readable in an ad-hoc query
        // and survive any later reordering of their enum.
        entity.Property(notification => notification.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(notification => notification.TargetKind).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(notification => notification.TargetScreen).HasConversion<string>().HasMaxLength(64);

        // Optional for the reason the entity states: the column was added to a table that already held rows, and
        // nothing can say afterwards what condition one of those was raised for.
        entity.Property(notification => notification.Cause).HasConversion<string>().HasMaxLength(64);

        entity.Property(notification => notification.Title)
            .HasMaxLength(Notification.MaximumTitleLength)
            .IsRequired();
        entity.Property(notification => notification.Body)
            .HasMaxLength(Notification.MaximumBodyLength)
            .IsRequired();
        entity.Property(notification => notification.Source).HasMaxLength(Notification.MaximumSourceLength);
        entity.Property(notification => notification.DeduplicationKey)
            .HasMaxLength(NotificationDeduplicationKey.MaximumLength)
            .IsRequired();

        // Cascade rather than a statement in the erasure walk, for the reason the client's preferences cascade: what a
        // person was told is derived from them and goes when they do. It is also what makes the row reachable at all
        // by an erasure, since this table names no mail account and the walk enumerates the tables that do.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(notification => notification.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Optional, because most notifications lead to a screen or to nothing at all, and cascading so that the one
        // shape that does name a message cannot outlive it.
        entity.HasOne(notification => notification.TargetStoredEmail)
            .WithMany()
            .HasForeignKey(notification => notification.TargetStoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);

        // The same shape for the other record a notification can lead to, and cascading for the same reason: a
        // reminder about an event somebody deleted is a statement about a commitment that no longer exists, and the
        // delete confirmation already promises them it goes.
        entity.HasOne(notification => notification.TargetCalendarEvent)
            .WithMany()
            .HasForeignKey(notification => notification.TargetCalendarEventId)
            .OnDelete(DeleteBehavior.Cascade);

        // The third such record, on the same shape and cascading for the same reason: a reminder about a task
        // somebody erased is a statement about work nobody owes any more.
        entity.HasOne(notification => notification.TargetPersonalTask)
            .WithMany()
            .HasForeignKey(notification => notification.TargetPersonalTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // The send, the change, and the draft a failure is reported about, each cascading for the same reason: once the
        // record is no longer kept there is nothing left to decide about, and a row offering to open it would lead
        // nowhere. A draft somebody sent or discarded takes the statement about its copy with it this way.
        entity.HasOne(notification => notification.TargetOutgoingEmail)
            .WithMany()
            .HasForeignKey(notification => notification.TargetOutgoingEmailId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(notification => notification.TargetMailboxMutation)
            .WithMany()
            .HasForeignKey(notification => notification.TargetMailboxMutationId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(notification => notification.TargetMailDraft)
            .WithMany()
            .HasForeignKey(notification => notification.TargetMailDraftId)
            .OnDelete(DeleteBehavior.Cascade);

        // The deduplication rule, in the database rather than before the insert: a raise repeated while the first is
        // still unread passes any application check, and only the constraint closes that window. It is partial so that
        // reading the notification frees the condition to be said again when it recurs — and being partial is what
        // also makes it the index the unread count is answered from, since the count is one user's rows in it.
        entity.HasIndex(notification => new { notification.UserId, notification.DeduplicationKey })
            .IsUnique()
            .HasFilter($"NOT \"{nameof(NotificationEntity.IsRead)}\"")
            .HasDatabaseName(PersistenceConstraintNames.NotificationUnreadConditionUniqueIndexName);

        // The one index the centre is walked through, and it serves both readers: a page is one user's notifications
        // newest first, and retention erases the same user's oldest.
        entity.HasIndex(notification => new
        {
            notification.UserId,
            notification.OccurredAt,
            notification.Id,
        })
            .HasDatabaseName(PersistenceConstraintNames.NotificationTimelineIndexName);
    }
}
