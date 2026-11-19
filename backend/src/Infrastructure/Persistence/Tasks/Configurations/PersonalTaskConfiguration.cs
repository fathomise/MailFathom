// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Tasks;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Tasks.Configurations;

/// <summary>Declares what a person owes, and the one way a row leaves again.</summary>
/// <remarks>
/// <para>
/// The user's own row takes their tasks with them, so an erasure request never has to know this table exists — which
/// is also what makes the row reachable by an erasure at all, since this table names no mail account and the walk
/// enumerates the tables that do. That is the only cascade here, and the message a task cites is deliberately not a
/// second one: it is a plain value carrying no foreign key, for the reason
/// <c>mailbox_mutation_audit_entries</c> keeps its own message that way. A commitment read out of a thread is still a
/// commitment once the thread is gone, so erasing the mail must leave the task standing — while every association to a
/// stored message's identity cascades but for a reply's own parent, which <c>StoredEmailModelTests</c> holds the model
/// to. A reader resolving a citation whose message has been erased finds nothing under that identity, exactly as a job
/// whose payload names erased mail does.
/// </para>
/// <para>
/// What makes the row personal data is the title, which may be a sentence read out of a body, the anchor's label,
/// which is a subject or an event title kept as it was drawn, and the counterpart, which names a person by address and
/// name. Nothing else here is mail: the due date and the plan are times, the origin, the direction, and the completion
/// are MailFathom's own state, and the message, the thread, the event, and the reply are cited by identifier rather
/// than copied.
/// </para>
/// </remarks>
internal sealed class PersonalTaskConfiguration : IEntityTypeConfiguration<PersonalTaskEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PersonalTaskEntity> entity)
    {
        // Each pair below is one value the domain holds whole, so half of one names nothing: a plan is a start and a
        // length, an anchor is one thread or one event with the words it was drawn with, a counterpart is an address and
        // its comparison form, and an answer is a reply and when it arrived.
        entity.ToTable(
            "tasks",
            table =>
            {
                table.HasCheckConstraint(
                    PersistenceConstraintNames.PersonalTaskPlanCompleteCheckConstraintName,
                    $"(\"{nameof(PersonalTaskEntity.PlannedStartAt)}\" IS NULL) = (\"{nameof(PersonalTaskEntity.PlannedMinutes)}\" IS NULL)");
                table.HasCheckConstraint(
                    PersistenceConstraintNames.PersonalTaskSingleAnchorCheckConstraintName,
                    $"\"{nameof(PersonalTaskEntity.AnchorThreadId)}\" IS NULL OR \"{nameof(PersonalTaskEntity.AnchorEventId)}\" IS NULL");
                table.HasCheckConstraint(
                    PersistenceConstraintNames.PersonalTaskAnchorLabelledCheckConstraintName,
                    $"(\"{nameof(PersonalTaskEntity.AnchorLabel)}\" IS NULL) = (\"{nameof(PersonalTaskEntity.AnchorThreadId)}\" IS NULL AND \"{nameof(PersonalTaskEntity.AnchorEventId)}\" IS NULL)");
                table.HasCheckConstraint(
                    PersistenceConstraintNames.PersonalTaskCounterpartCompleteCheckConstraintName,
                    $"(\"{nameof(PersonalTaskEntity.CounterpartAddress)}\" IS NULL) = (\"{nameof(PersonalTaskEntity.CounterpartNormalizedAddress)}\" IS NULL)");
                // The domain refuses to restore an answer on anything but a completed wait, so a row breaking that rule
                // would fail every later read of the person's list rather than only the write that produced it.
                table.HasCheckConstraint(
                    PersistenceConstraintNames.PersonalTaskAnswerClosesAWaitCheckConstraintName,
                    $"\"{nameof(PersonalTaskEntity.AnswerStoredEmailId)}\" IS NULL OR (\"{nameof(PersonalTaskEntity.Direction)}\" = '{nameof(PersonalTaskDirection.Awaited)}' AND \"{nameof(PersonalTaskEntity.IsCompleted)}\")");
                table.HasCheckConstraint(
                    PersistenceConstraintNames.PersonalTaskAnswerCompleteCheckConstraintName,
                    $"(\"{nameof(PersonalTaskEntity.AnswerStoredEmailId)}\" IS NULL) = (\"{nameof(PersonalTaskEntity.AnsweredAt)}\" IS NULL)");
            });
        entity.HasKey(task => task.Id);
        entity.Property(task => task.Id).ValueGeneratedNever();

        // Stored as text for the reason every other enum in this model is: it stays readable in an ad-hoc query and
        // survives any later reordering of its enum.
        entity.Property(task => task.Origin).HasConversion<string>().HasMaxLength(64).IsRequired();

        entity.Property(task => task.Title).HasMaxLength(PersonalTask.MaximumTitleLength).IsRequired();

        // Defaulted in the database so every task that existed before the column is owed, which is what it was.
        entity.Property(task => task.Direction)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(PersonalTaskDirection.Owed)
            .IsRequired();
        entity.Property(task => task.AnchorLabel).HasMaxLength(TaskAnchor.MaximumLabelLength);
        entity.Property(task => task.CounterpartAddress).HasMaxLength(StoredEmailEntity.MaximumAddressLength);
        entity.Property(task => task.CounterpartNormalizedAddress).HasMaxLength(StoredEmailEntity.MaximumAddressLength);
        entity.Property(task => task.CounterpartDisplayName).HasMaxLength(TaskCounterpart.MaximumDisplayNameLength);

        // Cascade rather than a statement in the erasure walk, for the reason the notification table cascades: what a
        // person owes belongs to them and goes when they do.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(task => task.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The one index the list is walked through, in the order the list is drawn: one person's tasks, soonest due
        // first, with PostgreSQL's own NULLS LAST putting the undated ones at the end and the identifier breaking a
        // tie. Reading the proposals apart from the commitments filters within one person's rows, which is a handful
        // of them, so the origin earns no column of its own here.
        entity.HasIndex(task => new { task.UserId, task.DueOn, task.Id })
            .HasDatabaseName(PersistenceConstraintNames.PersonalTaskDueOrderIndexName);

        // The anchor, the plan, and the counterpart are each something one surface reads one person's tasks by: a
        // thread or an event shows what is owed about it, the calendar reads a window of plans, and a person's page
        // reads what is owed to and by them. None of them carries a foreign key, for the reason the cited message
        // carries none.
        entity.HasIndex(task => new { task.UserId, task.AnchorThreadId });
        entity.HasIndex(task => new { task.UserId, task.AnchorEventId });
        entity.HasIndex(task => new { task.UserId, task.PlannedStartAt });
        entity.HasIndex(task => new { task.UserId, task.CounterpartNormalizedAddress });

        // Settling a reply asks which waits a thread answers across every user, so the user-led index above cannot
        // serve it. Partial on the open waits, which are the only rows the question can match and a small part of the
        // table once a list has any history.
        entity.HasIndex(task => task.AnchorThreadId)
            .HasDatabaseName(PersistenceConstraintNames.PersonalTaskAwaitingAnswerIndexName)
            .HasFilter($"\"{nameof(PersonalTaskEntity.Direction)}\" = '{nameof(PersonalTaskDirection.Awaited)}' AND NOT \"{nameof(PersonalTaskEntity.IsCompleted)}\"");
    }
}
