// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.StoredFiles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Files;

/// <summary>Covers how a conversation takes a file in, which files a question may carry, and what deleting a conversation takes with it.</summary>
public sealed class AgentConversationFilesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly AgentConversationId Conversation = AgentConversationId.Create(new Guid("019a0000-0000-7000-8000-0000000000c1"));

    private static readonly StoredFileId Stored = StoredFileId.Create(new Guid("019a0000-0000-7000-8000-0000000000f1"));

    private readonly InMemoryAgentConversationFileStore store = new();

    private readonly IAgentConversationStore conversations = Substitute.For<IAgentConversationStore>();

    private readonly IStoredFileStore storedFiles = Substitute.For<IStoredFileStore>();

    private readonly IAttachmentTextExtractor extractor = Substitute.For<IAttachmentTextExtractor>();

    public AgentConversationFilesTests() =>
        this.storedFiles
            .WriteAsync(SyntheticUser.Deployment, Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(Stored);

    /// <summary>A file refused for what its octets are is refused before anything is parsed, stored, or started.</summary>
    [Theory]
    [InlineData(0, AgentConversationFileRefusal.Empty)]
    [InlineData(1, AgentConversationFileRefusal.TooLarge)]
    [InlineData(2, AgentConversationFileRefusal.KindNotTaken)]
    public async Task TakeAsync_OctetsNoConversationTakes_RefusesThemWithoutStoringOrStartingAnything(int shape, AgentConversationFileRefusal expected)
    {
        // Arrange
        byte[] content = shape switch
        {
            0 => [],
            1 => new byte[AgentConversationFileBounds.MaximumFileOctets + 1],
            _ => [0x00, 0x01, 0x02, 0xFF, 0xFE, 0x00, 0x7F],
        };

        // Act
        var intake = await this.Files().TakeAsync(Conversation, SyntheticUser.Deployment, "file.bin", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, intake.Refusal);
        Assert.Empty(this.storedFiles.ReceivedCalls());
        Assert.Empty(this.conversations.ReceivedCalls());
    }

    /// <summary>A short document is read once, at intake, and recorded with its words beside the stored file holding its octets.</summary>
    [Fact]
    public async Task TakeAsync_AShortTextDocument_RecordsItReadWholeUnderTheStoredFilesIdentity()
    {
        // Arrange
        const string Text = "The supplier quoted forty euros.";
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.Extracted(new ExtractedAttachmentText(Text, 1, [], [])));

        // Act
        var intake = await this.Files().TakeAsync(Conversation, SyntheticUser.Deployment, "quote.txt", Encoding.UTF8.GetBytes(Text), TestContext.Current.CancellationToken);

        // Assert
        var taken = Assert.IsType<AgentConversationFile>(intake.File);
        Assert.Equal(AgentConversationFileId.Of(Stored), taken.Id);
        Assert.Equal(("quote.txt", AgentConversationFileKind.Document, "text/plain"), (taken.Name, taken.Kind, taken.MediaType));
        Assert.Equal((true, false), (taken.Reading!.WasRead, taken.Reading.IsLong));
        var recorded = await this.store.ReadTextAsync(Conversation, SyntheticUser.Deployment, taken.Id, TestContext.Current.CancellationToken);
        Assert.Equal(Text, recorded?.Text);
    }

    /// <summary>A picture is recognized from its octets, never read for words, and recorded as what the octets proved.</summary>
    [Fact]
    public async Task TakeAsync_APictureNamedAsADocument_RecordsItAsTheImageItsOctetsAre()
    {
        // Act
        var intake = await this.Files().TakeAsync(Conversation, SyntheticUser.Deployment, "report.pdf", SyntheticImages.Png(8, 8), TestContext.Current.CancellationToken);

        // Assert
        var taken = Assert.IsType<AgentConversationFile>(intake.File);
        Assert.Equal((AgentConversationFileKind.Picture, "image/png", null), (taken.Kind, taken.MediaType, taken.Reading));
        Assert.Empty(this.extractor.ReceivedCalls());
    }

    /// <summary>A conversation already holding as many files as one may refuses the next before parsing or storing it.</summary>
    [Fact]
    public async Task TakeAsync_AConversationAlreadyFull_RefusesTheFileWithoutStoringIt()
    {
        // Arrange
        foreach (var index in Enumerable.Range(1, AgentConversationFileBounds.MaximumFilesPerConversation))
        {
            this.store.Hold(Conversation, SyntheticUser.Deployment, Held(new Guid($"019a0000-0000-7000-8000-{index:x12}"), AgentConversationFileKind.Document));
        }

        // Act
        var intake = await this.Files().TakeAsync(Conversation, SyntheticUser.Deployment, "one-more.png", SyntheticImages.Png(8, 8), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentConversationFileRefusal.ConversationFull, intake.Refusal);
        Assert.Empty(this.storedFiles.ReceivedCalls());
    }

    /// <summary>A row the store refuses to record leaves no stored file behind, so a lost race for the last place orphans nothing.</summary>
    [Fact]
    public async Task TakeAsync_TheStoreRefusingTheRow_RemovesTheStoredFileAgain()
    {
        // Arrange
        var refusing = Substitute.For<IAgentConversationFileStore>();
        refusing.ListAsync(Conversation, SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns([]);
        refusing
            .RecordAsync(Conversation, SyntheticUser.Deployment, Arg.Any<AgentConversationFileText>(), Arg.Any<CancellationToken>())
            .Returns(AgentConversationFileRefusal.ConversationFull);

        // Act
        var intake = await this.Files(refusing).TakeAsync(Conversation, SyntheticUser.Deployment, "late.png", SyntheticImages.Png(8, 8), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentConversationFileRefusal.ConversationFull, intake.Refusal);
        await this.storedFiles.Received(1).RemoveAsync(SyntheticUser.Deployment, Stored, Arg.Any<CancellationToken>());
    }

    /// <summary>A question carries only files its conversation holds, each once, and no more of them than one question may.</summary>
    [Fact]
    public async Task ResolveCarriedAsync_NamedFiles_AnswersTheHeldOnesOnceAndRefusesAnUnknownOrTooMany()
    {
        // Arrange
        var held = new Guid("019a0000-0000-7000-8000-0000000000a1");
        this.store.Hold(Conversation, SyntheticUser.Deployment, Held(held, AgentConversationFileKind.Document));
        var files = this.Files();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var carried = await files.ResolveCarriedAsync(Conversation, SyntheticUser.Deployment, [held, held], cancellationToken);
        var unknown = await files.ResolveCarriedAsync(Conversation, SyntheticUser.Deployment, [held, Guid.CreateVersion7()], cancellationToken);
        var foreign = await files.ResolveCarriedAsync(Conversation, SyntheticUser.Another, [held], cancellationToken);
        var tooMany = await files.ResolveCarriedAsync(
            Conversation,
            SyntheticUser.Deployment,
            [.. Enumerable.Range(0, AgentConversationFileBounds.MaximumFilesPerQuestion + 1).Select(static _ => Guid.CreateVersion7())],
            cancellationToken);

        // Assert
        Assert.Equal([AgentConversationFileId.Create(held)], carried);
        Assert.Null(unknown);
        Assert.Null(foreign);
        Assert.Null(tooMany);
    }

    /// <summary>A picture's octets are read only for a picture this conversation of this person holds, so naming a document or another person's file reads nothing.</summary>
    [Fact]
    public async Task ReadPictureAsync_APictureHeldOrNot_ReadsOctetsOnlyForOneThisConversationHolds()
    {
        // Arrange
        var picture = new Guid("019a0000-0000-7000-8000-0000000000b1");
        var document = new Guid("019a0000-0000-7000-8000-0000000000b2");
        this.store.Hold(Conversation, SyntheticUser.Deployment, Held(picture, AgentConversationFileKind.Picture));
        this.store.Hold(Conversation, SyntheticUser.Deployment, Held(document, AgentConversationFileKind.Document));
        this.storedFiles
            .ReadAsync(SyntheticUser.Deployment, StoredFileId.Create(picture), Arg.Any<CancellationToken>())
            .Returns((ReadOnlyMemory<byte>?)SyntheticImages.Png(4, 4));
        var files = this.Files();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var shown = await files.ReadPictureAsync(Conversation, SyntheticUser.Deployment, AgentConversationFileId.Create(picture), cancellationToken);
        var notAPicture = await files.ReadPictureAsync(Conversation, SyntheticUser.Deployment, AgentConversationFileId.Create(document), cancellationToken);
        var foreign = await files.ReadPictureAsync(Conversation, SyntheticUser.Another, AgentConversationFileId.Create(picture), cancellationToken);

        // Assert
        Assert.NotNull(shown);
        Assert.Null(notAPicture);
        Assert.Null(foreign);
    }

    /// <summary>Deleting a conversation removes the stored file behind every file it held, and only once the conversation itself is gone.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteConversationAsync_AConversationHoldingFiles_RemovesTheirStoredFilesOnlyWhereItWasDeleted(bool deleted)
    {
        // Arrange
        var file = new Guid("019a0000-0000-7000-8000-0000000000d1");
        this.store.Hold(Conversation, SyntheticUser.Deployment, Held(file, AgentConversationFileKind.Document));
        this.conversations.TryDeleteAsync(Conversation, SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns(deleted);

        // Act
        var answered = await this.Files().DeleteConversationAsync(Conversation, SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(deleted, answered);
        await this.storedFiles.Received(deleted ? 1 : 0).RemoveAsync(SyntheticUser.Deployment, StoredFileId.Create(file), Arg.Any<CancellationToken>());
    }

    private static AgentConversationFileText Held(Guid id, AgentConversationFileKind kind) =>
        new(
            new AgentConversationFile(
                AgentConversationFileId.Create(id),
                "held",
                kind,
                kind is AgentConversationFileKind.Picture ? "image/png" : "text/plain",
                16,
                Now,
                kind is AgentConversationFileKind.Picture ? null : new AgentConversationFileReading(AttachmentTextExtractionOutcome.Extracted, 1, null, 5)),
            kind is AgentConversationFileKind.Picture ? string.Empty : "words",
            []);

    private AgentConversationFiles Files(IAgentConversationFileStore? files = null) =>
        new(
            this.conversations,
            files ?? this.store,
            this.storedFiles,
            this.extractor,
            new AgentConversationFilePassages(Substitute.For<IEmailTextChunker>(), EmailChunkingRules.Current),
            new ActiveEmbeddingSpace(
                Substitute.For<IActiveEmbeddingProfileReader>(),
                Substitute.For<IAiProviderHealthReader>(),
                new FakeTimeProvider(Now),
                textEmbeddingGenerator: null),
            new FakeTimeProvider(Now));
}
