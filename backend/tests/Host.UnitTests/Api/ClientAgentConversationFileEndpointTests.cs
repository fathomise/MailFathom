// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.AiProviders;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers how the client hands a file to a conversation and lists the files one holds.</summary>
public sealed class ClientAgentConversationFileEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Conversation = new("019a0000-0000-7000-8000-0000000000c1");

    private readonly InMemoryAgentConversationFileStore store = new();

    private readonly IStoredFileStore storedFiles = Substitute.For<IStoredFileStore>();

    private readonly IAttachmentTextExtractor extractor = Substitute.For<IAttachmentTextExtractor>();

    public ClientAgentConversationFileEndpointTests() =>
        this.storedFiles
            .WriteAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(StoredFileId.Create(new Guid("019a0000-0000-7000-8000-0000000000f1")));

    /// <summary>Every refusal answers the status a client can act on, and no wording that echoes the file.</summary>
    [Theory]
    [InlineData(AgentConversationFileRefusal.Empty, StatusCodes.Status400BadRequest)]
    [InlineData(AgentConversationFileRefusal.TooLarge, StatusCodes.Status413PayloadTooLarge)]
    [InlineData(AgentConversationFileRefusal.KindNotTaken, StatusCodes.Status415UnsupportedMediaType)]
    [InlineData(AgentConversationFileRefusal.ConversationFull, StatusCodes.Status409Conflict)]
    [InlineData(AgentConversationFileRefusal.NoSuchConversation, StatusCodes.Status404NotFound)]
    public void Refused_EachRefusal_AnswersItsOwnStatus(AgentConversationFileRefusal refusal, int expected)
    {
        // Act
        var answered = ClientAgentConversationFileEndpoints.Refused(refusal);

        // Assert
        Assert.Equal(expected, answered.StatusCode);
    }

    /// <summary>A document is taken from the request body, named by its disposition, and answered with what reading it came to.</summary>
    [Fact]
    public async Task TakeAsync_ATextBody_AnswersCreatedWithTheDocumentReadWhole()
    {
        // Arrange
        const string Text = "The supplier quoted forty euros.";
        this.extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(AttachmentTextExtractionResult.Extracted(new ExtractedAttachmentText(Text, 1, [], [])));
        var context = Upload(Encoding.UTF8.GetBytes(Text), "attachment; filename=\"quote.txt\"");

        // Act
        var answered = await ClientAgentConversationFileEndpoints.TakeAsync(Conversation, Resolver(), this.Files(), context, TestContext.Current.CancellationToken);

        // Assert
        var created = Assert.IsType<Created<ClientAgentConversationFileResponse>>(answered.Result);
        Assert.Equal(("quote.txt", "document", "whole"), (created.Value!.Name, created.Value.Kind, created.Value.Text?.State));
    }

    [Fact]
    public async Task TakeAsync_AnEmptyBody_AnswersBadRequest()
    {
        // Act
        var answered = await ClientAgentConversationFileEndpoints.TakeAsync(Conversation, Resolver(), this.Files(), Upload([], null), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
    }

    /// <summary>A name outside ASCII travels in the encoded parameter, which is preferred over the fallback beside it.</summary>
    [Fact]
    public void NameOf_ADispositionCarryingAnEncodedName_ReadsTheEncodedOne()
    {
        // Arrange
        var context = Upload([1], "attachment; filename=\"oferta.pdf\"; filename*=UTF-8''oferta%20%C5%BC%C3%B3%C5%82ta.pdf");

        // Act
        var name = ClientAgentConversationFileEndpoints.NameOf(context.Request);

        // Assert
        Assert.Equal("oferta żółta.pdf", name);
    }

    /// <summary>Listing reads only this person's conversation, so another person's files answer as none.</summary>
    [Fact]
    public async Task ListAsync_FilesOfAnotherPerson_AnswersNone()
    {
        // Arrange
        this.store.Hold(
            AgentConversationId.Create(Conversation),
            SyntheticUser.Another,
            new AgentConversationFileText(
                new AgentConversationFile(AgentConversationFileId.Create(Guid.CreateVersion7()), "theirs.txt", AgentConversationFileKind.Document, "text/plain", 5, Now, Reading: null),
                "words",
                []));

        // Act
        var answered = await ClientAgentConversationFileEndpoints.ListAsync(Conversation, Resolver(), this.Files(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(answered.Value!.Files);
    }

    private static DefaultHttpContext Upload(byte[] body, string? disposition)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(body);

        if (disposition is not null)
        {
            context.Request.Headers.ContentDisposition = disposition;
        }

        return context;
    }

    private static MailboxScopeResolver Resolver() =>
        new(
            AssignedMailAccountCatalogs.For(
                AccessAuthorizations.ForUserGranted(SyntheticUser.Deployment, MailFathomPermission.AssistantUse),
                SyntheticServedAccount.Of("primary")),
            StubMailFolderParticipation.Nothing,
            StubDeploymentMailFolders.None,
            StubMailFolderMappings.Nothing.Resolver);

    private AgentConversationFiles Files() =>
        new(
            Substitute.For<IAgentConversationStore>(),
            this.store,
            this.storedFiles,
            this.extractor,
            new AgentConversationFilePassages(Substitute.For<IEmailTextChunker>(), EmailChunkingRules.Current, EmbeddingInputBound.Default),
            new ActiveEmbeddingSpace(
                Substitute.For<IActiveEmbeddingProfileReader>(),
                Substitute.For<IAiProviderHealthReader>(),
                new FakeTimeProvider(Now),
                textEmbeddingGenerator: null),
            new FakeTimeProvider(Now));
}
