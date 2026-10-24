// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.ReplyDrafts;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.ReplyDrafts;

/// <summary>Covers which messages of a conversation a drafting keeps, and how their attachments share a message's bound.</summary>
/// <remarks>
/// The selection is composed here and evaluated by PostgreSQL, so what these tests establish is which rows it keeps
/// rather than what SQL it becomes; the assembly is the in-process half that marks and bounds what the query read.
/// </remarks>
public sealed class ReplyDraftSourceStoreTests
{
    private const int CharactersPerMessage = 20;

    private static readonly DateTimeOffset Monday = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A reply to an early message of a long exchange is grounded in that message, not only in the ones after it.</summary>
    [Fact]
    public void ChosenAndMostRecent_AnOldChosenMessage_IsKeptBesideTheMostRecentOthers()
    {
        // Arrange
        var emails = Enumerable.Range(0, 5).Select(day => Email(Monday.AddDays(day))).ToArray();

        // Act
        var kept = ReplyDraftSourceStore
            .ChosenAndMostRecent(emails.AsQueryable(), StoredEmailId.Create(emails[0].Id), count: 3)
            .Select(static email => email.Id)
            .ToArray();

        // Assert
        Assert.Equal([emails[0].Id, emails[4].Id, emails[3].Id], kept);
    }

    /// <summary>The turn is numbered oldest first whatever order the read kept the rows in, and only the chosen message is marked.</summary>
    [Fact]
    public void Assemble_RowsReadChosenFirst_NumbersThemOldestFirstAndMarksTheChosenOne()
    {
        // Arrange
        var older = Row(Monday, "older");
        var newer = Row(Monday.AddDays(1), "newer");

        // Act
        var sources = ReplyDraftSourceStore.Assemble(
            [newer, older],
            StoredEmailId.Create(older.StoredEmailId),
            [],
            CharactersPerMessage,
            sendingNormalizedAddress: null,
            []);

        // Assert
        Assert.Equal(["older", "newer"], sources.Messages.Select(static message => message.Text));
        Assert.Equal([true, false], sources.Messages.Select(static message => message.IsChosen));
        Assert.Equal([0, 1], sources.Messages.Select(static message => message.Position));
    }

    /// <summary>Attachments fill what a message's own text left of its bound, in the order the message carries them, and no more.</summary>
    [Fact]
    public void Assemble_AttachmentsPastWhatTheTextLeft_AreCutToTheMessagesBound()
    {
        // Arrange
        var message = Row(Monday, "twelve chars");
        var attachments = new[]
        {
            Attachment(message, "offer.pdf", "123456"),
            Attachment(message, "terms.pdf", "abcdef"),
            Attachment(message, "late.pdf", "never read"),
        };

        // Act
        var sources = ReplyDraftSourceStore.Assemble(
            [message],
            StoredEmailId.Create(message.StoredEmailId),
            attachments,
            CharactersPerMessage,
            sendingNormalizedAddress: null,
            []);

        // Assert
        var carried = Assert.Single(sources.Messages).Attachments;
        Assert.Equal(["offer.pdf", "terms.pdf"], carried.Select(static attachment => attachment.FileName));
        Assert.Equal(["123456", "ab"], carried.Select(static attachment => attachment.Text));
    }

    /// <summary>An attachment belongs to the message that carries it and to no other message of the conversation.</summary>
    [Fact]
    public void Assemble_AnAttachmentOfOneMessage_IsCarriedByThatMessageAlone()
    {
        // Arrange
        var withAttachment = Row(Monday, "see attached");
        var without = Row(Monday.AddDays(1), "thanks");

        // Act
        var sources = ReplyDraftSourceStore.Assemble(
            [withAttachment, without],
            StoredEmailId.Create(without.StoredEmailId),
            [Attachment(withAttachment, "offer.pdf", "total")],
            CharactersPerMessage,
            sendingNormalizedAddress: null,
            []);

        // Assert
        Assert.Equal([1, 0], sources.Messages.Select(static message => message.Attachments.Count));
    }

    private static StoredEmailEntity Email(DateTimeOffset sentAt) => new()
    {
        Id = Guid.CreateVersion7(),
        MailboxAccountId = "work",
        MailFolderBinding = new MailFolderBindingEntity
        {
            MailboxAccountId = "work",
            Alias = "INBOX",
            RemotePath = "INBOX",
            MailboxAccount = new MailboxAccountEntity { Id = "work" },
        },
        SentAt = sentAt,
    };

    private static ReplyDraftSourceStore.ConversationMessageRow Row(DateTimeOffset sentAt, string text) =>
        new(Guid.CreateVersion7(), "The racking quotation", "Karolina", "karolina@example.test", sentAt, text);

    private static ReplyDraftSourceStore.AttachmentRow Attachment(
        ReplyDraftSourceStore.ConversationMessageRow message,
        string fileName,
        string text) =>
        new(message.StoredEmailId, fileName, AttachmentTextKind.Document, text);
}
