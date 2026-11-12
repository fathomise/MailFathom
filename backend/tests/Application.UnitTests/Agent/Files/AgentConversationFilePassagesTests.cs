// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Files;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction.Attachments;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Files;

/// <summary>Covers how a conversation's document is cut, and how much of it a provider is asked to embed.</summary>
public sealed class AgentConversationFilePassagesTests
{
    private const int PageLength = 600;

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly IEmailTextChunker chunker = Substitute.For<IEmailTextChunker>();

    public AgentConversationFilePassagesTests() =>
        this.chunker
            .DeriveAttachmentChunks(Arg.Any<string>(), Arg.Any<EmailChunkingRules>(), Arg.Any<EmbeddingInputBound>(), Arg.Any<EmailChunkAttachmentSource>())
            .Returns(EmailChunkingResult.NoText);

    /// <summary>One allowance is spent across the pages, so a file of many pages is embedded no further than one message's text is.</summary>
    [Fact]
    public void CutForEmbedding_PagesPastTheBound_SpendsOneAllowanceAcrossThemAndCutsNoPageBeyondIt()
    {
        // Arrange
        var passages = new AgentConversationFilePassages(this.chunker, EmailChunkingRules.Current, EmbeddingInputBound.Create(1_000));

        // Act
        passages.CutForEmbedding(ThreePages());

        // Assert
        Assert.Equal([PageLength, 400], this.BoundsCut());
    }

    /// <summary>The search by words reaches every page, because it costs no provider anything.</summary>
    [Fact]
    public void Cut_PagesPastTheBound_CutsEveryPageWhole()
    {
        // Arrange
        var passages = new AgentConversationFilePassages(this.chunker, EmailChunkingRules.Current, EmbeddingInputBound.Create(1_000));

        // Act
        passages.Cut(ThreePages());

        // Assert
        Assert.Equal([PageLength, PageLength, PageLength], this.BoundsCut());
    }

    private static AgentConversationFileText ThreePages() =>
        new(
            new AgentConversationFile(
                AgentConversationFileId.Create(new Guid("019a0000-0000-7000-8000-0000000000d1")),
                "report.pdf",
                AgentConversationFileKind.Document,
                "application/pdf",
                3 * PageLength,
                Now,
                Reading: null),
            new string('a', 3 * PageLength),
            [.. Enumerable.Range(0, 3).Select(static page => new AttachmentTextSegment(AttachmentTextSegmentKind.Page, page + 1, Label: null, StartOffset: page * PageLength))]);

    private int[] BoundsCut() =>
        [.. this.chunker.ReceivedCalls().Select(static call => ((EmbeddingInputBound)call.GetArguments()[2]!).MaximumCharacterCount)];
}
