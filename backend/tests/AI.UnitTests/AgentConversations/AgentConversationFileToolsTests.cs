// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Signals;
using MailFathom.Application.StoredFiles;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers what a turn is given of a conversation's files and what the two file tools answer.</summary>
public sealed class AgentConversationFileToolsTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly AgentConversationId Conversation = AgentConversationId.Create(new Guid("019a0000-0000-7000-8000-0000000000c1"));

    private static readonly AgentConversationFileId Note = AgentConversationFileId.Create(new Guid("019a0000-0000-7000-8000-0000000000a1"));

    private static readonly AgentConversationFileId Handbook = AgentConversationFileId.Create(new Guid("019a0000-0000-7000-8000-0000000000a2"));

    private static readonly AgentConversationFileId Picture = AgentConversationFileId.Create(new Guid("019a0000-0000-7000-8000-0000000000a3"));

    private static readonly AgentConversationFileId Scan = AgentConversationFileId.Create(new Guid("019a0000-0000-7000-8000-0000000000a4"));

    private static readonly byte[] PictureOctets = SyntheticImages.Png(8, 8);

    private readonly FakeTimeProvider clock = new(Now);

    private readonly InMemoryAgentConversationFileStore store = new();

    private readonly IStoredFileStore storedFiles = Substitute.For<IStoredFileStore>();

    private readonly ClientSignals signals;

    private readonly AgentAnswerJournal journal;

    public AgentConversationFileToolsTests()
    {
        this.signals = new ClientSignals([new RecordingClientSignalChannel()], this.clock);
        var conversations = Substitute.For<IAgentConversationStore>();
        conversations
            .AppendAsync(Conversation, SyntheticUser.Deployment, Arg.Any<AgentConversationEntry>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((long?)2);
        this.journal = new AgentAnswerJournal(
            Conversation,
            SyntheticUser.Deployment,
            AgentMessageId.New(),
            openedAt: 1,
            conversations,
            this.signals,
            Substitute.For<IUserLanguages>(),
            this.clock);

        this.store.Hold(Conversation, SyntheticUser.Deployment, Document(Note, "note.txt", "The supplier quoted forty euros for the lamps."));
        this.store.Hold(Conversation, SyntheticUser.Deployment, Document(Handbook, "handbook.txt", LongText()));
        this.store.Hold(Conversation, SyntheticUser.Deployment, new AgentConversationFileText(FileOf(Picture, "pass.png", AgentConversationFileKind.Picture, reading: null), string.Empty, []));
        this.store.Hold(
            Conversation,
            SyntheticUser.Deployment,
            new AgentConversationFileText(FileOf(Scan, "scan.pdf", AgentConversationFileKind.Document, new AgentConversationFileReading(AttachmentTextExtractionOutcome.Encrypted, 0, null, 0)), string.Empty, []));
        this.storedFiles
            .ReadAsync(SyntheticUser.Deployment, Picture.StoredFile, Arg.Any<CancellationToken>())
            .Returns((ReadOnlyMemory<byte>?)PictureOctets);
    }

    /// <summary>A short document the question carries is given whole, a long one is only named as long, and a file the question does not carry gives no words at all.</summary>
    [Fact]
    public async Task Write_HeldFiles_GivesWordsWholeOnlyForAShortDocumentTheQuestionCarries()
    {
        // Arrange
        var ledger = new MailAnsweringRunLedger(MailAnsweringRunBounds.Default);
        var turnFiles = await this.ReadAsync([Note, Handbook], ledger);

        // Act
        var turn = Written(turnFiles, PictureOctets.Length);

        // Assert
        Assert.Contains("The supplier quoted forty euros for the lamps.", turn, StringComparison.Ordinal);
        Assert.DoesNotContain("Section 1", turn, StringComparison.Ordinal);
        Assert.Contains($"words=\"{AgentConversationFileReadings.LongWords}\"", turn, StringComparison.Ordinal);
        Assert.Equal("The supplier quoted forty euros for the lamps.".Length, ledger.Read().RetrievedCharacters);
    }

    /// <summary>A picture the question carries is shown to a model taking its octets and named as not shown to one that takes none, so the model can tell the person why.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pictures_ACarriedPicture_IsShownOnlyWithinTheModelsImageOctets(bool fits)
    {
        // Arrange
        var turnFiles = await this.ReadAsync([Picture], new MailAnsweringRunLedger(MailAnsweringRunBounds.Default));
        var octets = fits ? PictureOctets.Length : 0;

        // Act
        var shown = turnFiles.Pictures(octets);
        var turn = Written(turnFiles, octets);

        // Assert
        Assert.Equal(fits ? 1 : 0, shown.Count);
        Assert.Contains($"shown=\"{(fits ? "true" : "false")}\"", turn, StringComparison.Ordinal);
    }

    /// <summary>A file nothing could read is named in the turn with the reason, and reading it answers the same rather than nothing.</summary>
    [Fact]
    public async Task ReadFile_AFileNothingCouldRead_NamesItAndWhyItWasNotRead()
    {
        // Arrange
        var turnFiles = await this.ReadAsync([Scan], new MailAnsweringRunLedger(MailAnsweringRunBounds.Default));
        var readFile = Tool(this.Tools(turnFiles), AgentConversationFileTools.ReadFileToolName);

        // Act
        var answer = (await readFile.InvokeAsync(new AIFunctionArguments { ["fileId"] = Scan.ToString() }, TestContext.Current.CancellationToken))?.ToString();

        // Assert
        Assert.Contains("scan.pdf", Written(turnFiles, 0), StringComparison.Ordinal);
        Assert.Contains(AgentConversationFileReadings.FileUnreadElementName, answer, StringComparison.Ordinal);
        Assert.Contains("scan.pdf", answer, StringComparison.Ordinal);
    }

    /// <summary>Only a file this conversation holds is read, so another conversation's identifier reaches nothing.</summary>
    [Fact]
    public async Task ReadFile_AnIdentifierThisConversationDoesNotHold_ReadsNothing()
    {
        // Arrange
        var foreign = AgentConversationFileId.Create(new Guid("019a0000-0000-7000-8000-0000000000ff"));
        this.store.Hold(AgentConversationId.Create(new Guid("019a0000-0000-7000-8000-0000000000c2")), SyntheticUser.Deployment, Document(foreign, "other.txt", "Another conversation's secret."));
        var readFile = Tool(this.Tools(await this.ReadAsync([], new MailAnsweringRunLedger(MailAnsweringRunBounds.Default))), AgentConversationFileTools.ReadFileToolName);

        // Act
        var answer = (await readFile.InvokeAsync(new AIFunctionArguments { ["fileId"] = foreign.ToString() }, TestContext.Current.CancellationToken))?.ToString();

        // Assert
        Assert.DoesNotContain("secret", answer, StringComparison.Ordinal);
    }

    /// <summary>A fact past what the turn gives of a long document is reached by searching it, and the passage found says it was found by its words.</summary>
    [Fact]
    public async Task SearchFiles_AFactDeepInALongDocument_FindsThePassageHoldingIt()
    {
        // Arrange
        var searchFiles = Tool(this.Tools(await this.ReadAsync([Handbook], new MailAnsweringRunLedger(MailAnsweringRunBounds.Default))), AgentConversationFileTools.SearchFilesToolName);

        // Act
        var answer = (await searchFiles.InvokeAsync(new AIFunctionArguments { ["query"] = "spare key bike shed" }, TestContext.Current.CancellationToken))?.ToString();

        // Assert
        Assert.Contains("drawer B", answer, StringComparison.Ordinal);
        Assert.Contains($"{AgentConversationFileReadings.FoundByAttributeName}=\"words\"", answer, StringComparison.Ordinal);
    }

    /// <summary>A conversation holding no document with words is offered neither file tool, so the model is not asked to choose one that answers nothing.</summary>
    [Fact]
    public async Task Create_AConversationHoldingOnlyAPictureAndAnUnreadFile_OffersNoFileTool()
    {
        // Arrange
        var bare = new InMemoryAgentConversationFileStore();
        bare.Hold(Conversation, SyntheticUser.Deployment, new AgentConversationFileText(FileOf(Picture, "pass.png", AgentConversationFileKind.Picture, reading: null), string.Empty, []));
        var files = ConversationFiles.Over(bare, this.storedFiles);
        var question = Question([]);
        var turnFiles = await AgentTurnFiles.ReadAsync(files, SensitiveContentEgressGuards.Inactive(), new MailAnsweringRunLedger(MailAnsweringRunBounds.Default), question, TestContext.Current.CancellationToken);

        // Act
        var offered = new AgentConversationFileTools(this.journal, files, SensitiveContentEgressGuards.Inactive(), new MailAnsweringRunLedger(MailAnsweringRunBounds.Default), question, turnFiles).Create();

        // Assert
        Assert.Empty(offered);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.journal.Dispose();

        return this.signals.DisposeAsync();
    }

    private static AgentQuestion Question(IReadOnlyList<AgentConversationFileId> carried) =>
        new(Conversation, SyntheticUser.Deployment, PresentationText.Create("What do my files say?"), Scope: null, AgentMessageId.New(), OpenedAt: 1, Now)
        {
            Files = carried,
        };

    private static string Written(AgentTurnFiles turnFiles, int maximumImageOctets)
    {
        using var document = new AgentTurnDocument();
        turnFiles.Write(document, maximumImageOctets);

        return document.Complete();
    }

    private static AIFunction Tool(IReadOnlyList<AIFunction> tools, string name) => Assert.Single(tools, tool => tool.Name == name);

    private static AgentConversationFile FileOf(AgentConversationFileId id, string name, AgentConversationFileKind kind, AgentConversationFileReading? reading) =>
        new(id, name, kind, kind is AgentConversationFileKind.Picture ? "image/png" : "text/plain", 64, Now, reading);

    private static AgentConversationFileText Document(AgentConversationFileId id, string name, string text) =>
        new(
            FileOf(id, name, AgentConversationFileKind.Document, new AgentConversationFileReading(AttachmentTextExtractionOutcome.Extracted, 1, null, text.Length)),
            text,
            []);

    private static string LongText() =>
        string.Concat(Enumerable.Range(1, 100).Select(static section => $"Section {section}. The facilities team checks this part of the building every Monday and logs what it finds in the register.\n\n"))
        + "Last section. The spare key for the bike shed is kept in room 2.14, drawer B.";

    private Task<AgentTurnFiles> ReadAsync(IReadOnlyList<AgentConversationFileId> carried, MailAnsweringRunLedger ledger) =>
        AgentTurnFiles.ReadAsync(ConversationFiles.Over(this.store, this.storedFiles), SensitiveContentEgressGuards.Inactive(), ledger, Question(carried), TestContext.Current.CancellationToken);

    private IReadOnlyList<AIFunction> Tools(AgentTurnFiles turnFiles) =>
        new AgentConversationFileTools(
            this.journal,
            ConversationFiles.Over(this.store, this.storedFiles),
            SensitiveContentEgressGuards.Inactive(),
            new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
            Question([]),
            turnFiles).Create();
}
