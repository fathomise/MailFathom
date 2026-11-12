// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Files;

/// <summary>Where the deployment records which files each conversation holds, what reading them came to, and where their passages sit in the active space.</summary>
/// <remarks>
/// <para>
/// The octets are not here: they are a stored file, placed by <see cref="IStoredFileStore" /> on whichever
/// backend <c>ContentStorage</c> selects, and what this records hangs off both that file and the conversation. So a file
/// goes with its conversation, and with its person, by the same cascades their other rows go by, and every vector placed
/// from it goes with it.
/// </para>
/// <para>
/// <strong>Every statement names the person as well as the conversation.</strong> A file of somebody else's conversation,
/// and a file of another conversation of the same person, read as absent, so nothing a model or a client names can
/// reach one.
/// </para>
/// </remarks>
public interface IAgentConversationFileStore
{
    /// <summary>Records a file into a conversation, beside the stored file already holding its octets.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="document">The file, with the words read out of it, which are empty for a picture or a document nothing could read.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="null" /> where it was recorded, or why it was not: the conversation is not this person's, or it already holds <see cref="AgentConversationFileBounds.MaximumFilesPerConversation" /> files.</returns>
    /// <remarks>The ceiling is checked in the statement that writes the row, while the conversation's own row is held, so two uploads at once cannot both take the last place.</remarks>
    Task<AgentConversationFileRefusal?> RecordAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileText document,
        CancellationToken cancellationToken);

    /// <summary>Lists the files one conversation holds, in the order they were taken in.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The files, and none where the conversation holds none or is not this person's.</returns>
    Task<IReadOnlyList<AgentConversationFile>> ListAsync(
        AgentConversationId conversation,
        UserId user,
        CancellationToken cancellationToken);

    /// <summary>Reads one document a conversation holds, with its words.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The file and its words, or <see langword="null" /> where this conversation of this person holds no such file.</returns>
    Task<AgentConversationFileText?> ReadTextAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileId file,
        CancellationToken cancellationToken);

    /// <summary>Reads every document of one conversation that yielded words, with those words.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The documents, in the order they were taken in.</returns>
    /// <remarks>Bounded by the files a conversation may hold and the characters the reader takes out of one, which is what makes a search by words over all of them an ordinary read.</remarks>
    Task<IReadOnlyList<AgentConversationFileText>> ReadTextsAsync(
        AgentConversationId conversation,
        UserId user,
        CancellationToken cancellationToken);

    /// <summary>Records where each passage of one document sits in a space.</summary>
    /// <param name="conversation">The conversation holding the document.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="profile">The space the vectors were placed in.</param>
    /// <param name="passages">The passages, each beside its vector.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the vectors are recorded, or once nothing was, where the file is gone or is not this person's.</returns>
    Task SaveEmbeddingsAsync(
        AgentConversationId conversation,
        UserId user,
        RegisteredEmbeddingProfile profile,
        IReadOnlyList<(AgentConversationFilePassage Passage, EmbeddingVector Vector)> passages,
        CancellationToken cancellationToken);

    /// <summary>Ranks one conversation's passages by how near they sit to a query in a space.</summary>
    /// <param name="conversation">The conversation, whose files are the only ones read.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="profile">The space the query was placed in, which is the only one whose vectors are read.</param>
    /// <param name="queryVector">Where the query sits.</param>
    /// <param name="limit">The most passages to return, at least one.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The passages, nearest first, each without its page, which the caller reads from the document's own places.</returns>
    Task<IReadOnlyList<AgentConversationFilePassage>> ReadNearestAsync(
        AgentConversationId conversation,
        UserId user,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken);
}
