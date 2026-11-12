// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the shared conversation file store, whose isolation every test of the Agent's file tools rests on.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement: a store answering another conversation's file would make a refusal
/// look missing from the tool, and one ranking backwards would make the search by meaning look broken.
/// </remarks>
public sealed class InMemoryAgentConversationFileStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly AgentConversationId Conversation = AgentConversationId.Create(new Guid("019a0000-0000-7000-8000-000000000001"));
    private static readonly AgentConversationId Other = AgentConversationId.Create(new Guid("019a0000-0000-7000-8000-000000000002"));

    [Fact]
    public async Task ReadTextsAsync_FilesHeldInSeveralConversations_AnswersOnlyThisConversationOfThisPerson()
    {
        // Arrange
        var store = new InMemoryAgentConversationFileStore();
        var mine = DocumentOf("01", "The supplier quoted forty euros.");
        store.Hold(Conversation, SyntheticUser.Deployment, mine);
        store.Hold(Other, SyntheticUser.Deployment, DocumentOf("02", "Another conversation's file."));
        store.Hold(Conversation, SyntheticUser.Another, DocumentOf("03", "Another person's file."));

        // Act
        var read = await store.ReadTextsAsync(Conversation, SyntheticUser.Deployment, TestContext.Current.CancellationToken);
        var foreign = await store.ReadTextAsync(Conversation, SyntheticUser.Deployment, DocumentOf("02", string.Empty).File.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([mine], read);
        Assert.Null(foreign);
    }

    [Fact]
    public async Task RecordAsync_AConversationAlreadyFull_RefusesTheNextFile()
    {
        // Arrange
        var store = new InMemoryAgentConversationFileStore();

        foreach (var index in Enumerable.Range(1, AgentConversationFileBounds.MaximumFilesPerConversation))
        {
            store.Hold(Conversation, SyntheticUser.Deployment, DocumentOf($"{index:x2}", "words"));
        }

        // Act
        var refusal = await store.RecordAsync(Conversation, SyntheticUser.Deployment, DocumentOf("ff", "words"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentConversationFileRefusal.ConversationFull, refusal);
    }

    [Fact]
    public async Task ReadNearestAsync_PassagesPlacedInTheProfile_RanksTheNearestFirstWithoutItsPage()
    {
        // Arrange
        var store = new InMemoryAgentConversationFileStore();
        var document = DocumentOf("01", "near passage. far passage.");
        store.Hold(Conversation, SyntheticUser.Deployment, document);
        var profile = Profile();
        var near = new AgentConversationFilePassage(document.File.Id, 0, 0, 13, Segment: null);
        var far = new AgentConversationFilePassage(document.File.Id, 1, 14, 26, Segment: null);
        await store.SaveEmbeddingsAsync(
            Conversation,
            SyntheticUser.Deployment,
            profile,
            [(far, EmbeddingVector.Create([0f, 1f])), (near, EmbeddingVector.Create([1f, 0.1f]))],
            TestContext.Current.CancellationToken);

        // Act
        var nearest = await store.ReadNearestAsync(Conversation, SyntheticUser.Deployment, profile, EmbeddingVector.Create([1f, 0f]), 2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([near, far], nearest);
    }

    private static AgentConversationFileText DocumentOf(string suffix, string text) =>
        new(
            new AgentConversationFile(
                AgentConversationFileId.Create(new Guid($"019a0000-0000-7000-8000-0000000000{suffix}")),
                "notes.txt",
                AgentConversationFileKind.Document,
                "text/plain",
                text.Length,
                Now,
                Reading: null),
            text,
            []);

    private static RegisteredEmbeddingProfile Profile() =>
        new(
            EmbeddingProfileId.Create(new Guid("019a0000-0000-7000-8000-0000000000aa")),
            EmbeddingProfileIdentity.Create(
                "a-provider",
                "a-model",
                modelVersion: null,
                dimension: 2,
                EmbeddingDistanceMetric.Cosine,
                EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true)));
}
