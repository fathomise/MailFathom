// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chunking;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Documents;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>Takes a case's files into its conversation the way a deployment's intake takes an upload, and serves them to the Agent's tools.</summary>
/// <remarks>
/// Every step a deployment takes is its own class: <see cref="AgentConversationFiles.TakeAsync" /> recognizes the
/// octets, the production attachment reader extracts the words, and the production chunker cuts a long document's
/// passages. What stands in for the database is the store of rows and the stored files, both held in memory. The
/// search by meaning is off, as on a deployment that embeds nothing and as the corpus search beside it is, so a search
/// of the files ranks by words alone and a fact reached here is one the Agent's own lookups reached.
/// </remarks>
internal static class CorpusConversationFiles
{
    /// <summary>Gets files no conversation holds.</summary>
    public static AgentConversationFiles None => Compose(new RecordingAgentConversationStore());

    /// <summary>Takes each file into one conversation, in order, and reads which of them the question carries.</summary>
    /// <param name="conversation">The conversation the question is asked in.</param>
    /// <param name="user">Whose conversation it is.</param>
    /// <param name="files">The files, as the person uploaded them.</param>
    /// <param name="cancellationToken">Withdraws the intake.</param>
    /// <returns>The files of conversations, holding these, and the identifiers of the ones the question carries.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the intake refuses a file, which a case writes none of.</exception>
    public static async Task<(AgentConversationFiles Files, IReadOnlyList<AgentConversationFileId> Carried)> TakeAsync(
        AgentConversationId conversation,
        UserId user,
        IReadOnlyList<ConversationFileUpload> files,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        var served = Compose(new RecordingAgentConversationStore());
        var carried = new List<AgentConversationFileId>();

        foreach (var file in files)
        {
            var intake = await served.TakeAsync(conversation, user, file.Name, file.Content, cancellationToken);

            if (intake.File is not { } taken)
            {
                throw new InvalidOperationException($"The intake refused the case's file \"{file.Name}\": {intake.Refusal}.");
            }

            if (file.Carried)
            {
                carried.Add(taken.Id);
            }
        }

        return (served, carried);
    }

    private static AgentConversationFiles Compose(IAgentConversationStore conversations) =>
        new(
            conversations,
            new InMemoryAgentConversationFileStore(),
            new HeldStoredFiles(),
            new BoundedAttachmentTextExtractor(new AttachmentTextExtractionOptions(), TimeProvider.System, NullLogger<BoundedAttachmentTextExtractor>.Instance),
            new AgentConversationFilePassages(new DeterministicEmailTextChunker(), EmailChunkingRules.Current),
            new ActiveEmbeddingSpace(new NothingEmbedded(), new NothingEmbedded(), TimeProvider.System, textEmbeddingGenerator: null),
            TimeProvider.System);

    /// <summary>Stands for a deployment that activated no embedding profile, so nothing past the profile is asked.</summary>
    private sealed class NothingEmbedded : IActiveEmbeddingProfileReader, IAiProviderHealthReader
    {
        public Task<RegisteredEmbeddingProfile?> FindActiveProfileAsync(CancellationToken cancellationToken) =>
            Task.FromResult<RegisteredEmbeddingProfile?>(null);

        public AiProviderHealth Read(AiProviderRole role) =>
            throw new NotSupportedException("No profile is active, so no embedding provider is ever asked about.");
    }

    /// <summary>Holds the octets of the files a case uploads, which only a picture is ever read back from.</summary>
    private sealed class HeldStoredFiles : IStoredFileStore
    {
        private readonly Dictionary<StoredFileId, (UserId Owner, ReadOnlyMemory<byte> Content)> held = [];

        public Task<StoredFileId?> WriteAsync(UserId owner, string mediaType, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            // Minted in order rather than at random, because a file's identifier is written into the turn and a run's
            // answers are cached under what the turn says.
            var file = StoredFileId.Create(new Guid($"0199f11e-0000-7000-8000-{this.held.Count + 1:x12}"));
            this.held[file] = (owner, content.ToArray());

            return Task.FromResult<StoredFileId?>(file);
        }

        public Task<ReadOnlyMemory<byte>?> ReadAsync(UserId owner, StoredFileId file, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>?>(this.held.TryGetValue(file, out var stored) && stored.Owner == owner ? stored.Content : null);

        public Task<bool> HoldsAsync(UserId owner, StoredFileId file, CancellationToken cancellationToken) =>
            Task.FromResult(this.held.TryGetValue(file, out var stored) && stored.Owner == owner);

        public Task RemoveAsync(UserId owner, StoredFileId file, CancellationToken cancellationToken)
        {
            this.held.Remove(file);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HeldStoredFile>> FindUnmentionedAsync(DateTimeOffset writtenBefore, int maxFiles, CancellationToken cancellationToken) =>
            throw new NotSupportedException("A case's run sweeps nothing.");
    }
}
