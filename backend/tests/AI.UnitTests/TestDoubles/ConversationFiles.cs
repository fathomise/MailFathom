// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chunking;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.StoredFiles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Composes the files of conversations over a store held in memory, with the production chunker cutting their passages.</summary>
internal static class ConversationFiles
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Gets files no conversation holds, which is what a deployment nobody uploaded to answers.</summary>
    public static AgentConversationFiles None => Over(new InMemoryAgentConversationFileStore());

    /// <summary>Composes the files over a store, reading pictures' octets from the stored files given.</summary>
    /// <param name="store">The rows of the files.</param>
    /// <param name="storedFiles">The octets, which only a picture is read from; none where omitted.</param>
    /// <param name="space">The space a search by meaning places its query in; one placing nothing where omitted.</param>
    /// <returns>The files.</returns>
    public static AgentConversationFiles Over(
        InMemoryAgentConversationFileStore store,
        IStoredFileStore? storedFiles = null,
        ActiveEmbeddingSpace? space = null) =>
        new(
            Substitute.For<IAgentConversationStore>(),
            store,
            storedFiles ?? Substitute.For<IStoredFileStore>(),
            Substitute.For<IAttachmentTextExtractor>(),
            new AgentConversationFilePassages(new DeterministicEmailTextChunker(), EmailChunkingRules.Current, EmbeddingInputBound.Default),
            space ?? new ActiveEmbeddingSpace(
                Substitute.For<IActiveEmbeddingProfileReader>(),
                Substitute.For<IAiProviderHealthReader>(),
                new FakeTimeProvider(Now),
                textEmbeddingGenerator: null),
            new FakeTimeProvider(Now));
}
