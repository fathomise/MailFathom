// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Holds the files of conversations in memory, each under the conversation and the person it was taken for.</summary>
/// <remarks>
/// A conversation is taken to be its person's once a file is held in it, so a read naming another conversation or another
/// person reaches nothing, which is the property the real store's join on the conversation's owner gives. Nearness is
/// cosine similarity over the vectors held for the one profile asked about.
/// </remarks>
internal sealed class InMemoryAgentConversationFileStore : IAgentConversationFileStore
{
    private readonly List<(AgentConversationId Conversation, UserId User, AgentConversationFileText Document)> held = [];
    private readonly List<(AgentConversationFileId File, EmbeddingProfileId Profile, AgentConversationFilePassage Passage, EmbeddingVector Vector)> vectors = [];

    /// <summary>Gets the passages whose vectors were saved, which is what proves a long document was placed.</summary>
    internal IReadOnlyList<AgentConversationFilePassage> PlacedPassages => [.. this.vectors.Select(static placed => placed.Passage)];

    /// <summary>Holds a file in a conversation without going through the port, as an earlier upload would have.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it is.</param>
    /// <param name="document">The file and its words.</param>
    internal void Hold(AgentConversationId conversation, UserId user, AgentConversationFileText document) =>
        this.held.Add((conversation, user, document));

    /// <inheritdoc />
    public Task<AgentConversationFileRefusal?> RecordAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileText document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (this.HeldIn(conversation, user).Count() >= AgentConversationFileBounds.MaximumFilesPerConversation)
        {
            return Task.FromResult<AgentConversationFileRefusal?>(AgentConversationFileRefusal.ConversationFull);
        }

        this.Hold(conversation, user, document);

        return Task.FromResult<AgentConversationFileRefusal?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentConversationFile>> ListAsync(
        AgentConversationId conversation,
        UserId user,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentConversationFile>>([.. this.HeldIn(conversation, user).Select(static document => document.File)]);

    /// <inheritdoc />
    public Task<AgentConversationFileText?> ReadTextAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileId file,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.HeldIn(conversation, user).FirstOrDefault(document => document.File.Id == file && document.File.Kind is AgentConversationFileKind.Document));

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentConversationFileText>> ReadTextsAsync(
        AgentConversationId conversation,
        UserId user,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentConversationFileText>>([.. this.HeldIn(conversation, user).Where(static document => document.Text.Length > 0)]);

    /// <inheritdoc />
    public Task SaveEmbeddingsAsync(
        AgentConversationId conversation,
        UserId user,
        RegisteredEmbeddingProfile profile,
        IReadOnlyList<(AgentConversationFilePassage Passage, EmbeddingVector Vector)> passages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(passages);

        var files = this.HeldIn(conversation, user).Select(static document => document.File.Id).ToHashSet();

        this.vectors.AddRange(passages
            .Where(placed => files.Contains(placed.Passage.File))
            .Select(placed => (placed.Passage.File, profile.Id, placed.Passage, placed.Vector)));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentConversationFilePassage>> ReadNearestAsync(
        AgentConversationId conversation,
        UserId user,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queryVector);

        var files = this.HeldIn(conversation, user).Select(static document => document.File.Id).ToHashSet();

        return Task.FromResult<IReadOnlyList<AgentConversationFilePassage>>(
        [
            .. this.vectors
                .Where(placed => placed.Profile == profile.Id && files.Contains(placed.File))
                .OrderByDescending(placed => CosineOf(placed.Vector, queryVector))
                .Take(limit)
                .Select(static placed => placed.Passage with { Segment = null }),
        ]);
    }

    private static double CosineOf(EmbeddingVector left, EmbeddingVector right)
    {
        float[] first = [.. left.Components.Span];
        float[] second = [.. right.Components.Span];
        var dot = first.Zip(second, static (component, other) => (double)component * other).Sum();
        var norms = Math.Sqrt(first.Sum(static value => (double)value * value)) * Math.Sqrt(second.Sum(static value => (double)value * value));

        return norms is 0 ? 0 : dot / norms;
    }

    private IEnumerable<AgentConversationFileText> HeldIn(AgentConversationId conversation, UserId user) =>
        this.held
            .Where(entry => entry.Conversation == conversation && entry.User == user)
            .Select(static entry => entry.Document);
}
