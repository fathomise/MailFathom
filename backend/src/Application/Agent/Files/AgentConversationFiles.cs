// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Agent.Files;

/// <summary>Takes files into a person's conversations with the Agent, reads them back, and removes them with their conversation.</summary>
/// <remarks>
/// <para>
/// <strong>A file is taken in once and read then.</strong> Its kind is proven from its octets, its words are read by
/// the attachment text reader that reads mail attachments, under that reader's own bounds, and what reading came to is
/// recorded beside the file — so a run answering a question reads recorded words rather than parsing a document, and a
/// document nothing could read says so from the moment it arrives. A long document's passages are placed in the active
/// vector space at the same time, for this conversation alone; a space that cannot place them leaves the document
/// searchable by its words, which is not a fault.
/// </para>
/// <para>
/// <strong>The octets are a stored file.</strong> They are written through <see cref="IStoredFileStore" />, so they go
/// wherever <c>ContentStorage</c> sends the next payload, are held on the terms every stored payload is held on, and
/// are moved and released with the rest. The stored file is written before the conversation's row that names it, and
/// removed again where that row is refused; one a crash leaves unnamed is what the sweep of unlinked files removes.
/// </para>
/// <para>
/// <strong>Nothing here logs.</strong> A file is something one identified person handed over, and it may hold anything.
/// </para>
/// </remarks>
public sealed class AgentConversationFiles
{
    private readonly IAgentConversationStore conversations;
    private readonly IAgentConversationFileStore files;
    private readonly IStoredFileStore storedFiles;
    private readonly IAttachmentTextExtractor extractor;
    private readonly AgentConversationFilePassages passages;
    private readonly ActiveEmbeddingSpace space;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the files of conversations.</summary>
    /// <param name="conversations">Starts a conversation a first file arrives for, and removes one.</param>
    /// <param name="files">Records which files each conversation holds.</param>
    /// <param name="storedFiles">Holds the octets.</param>
    /// <param name="extractor">Reads a document's words, as it reads a mail attachment's.</param>
    /// <param name="passages">Cuts a long document into the passages it is searched and embedded by.</param>
    /// <param name="space">Places those passages, and a query, in the active vector space.</param>
    /// <param name="timeProvider">Stamps when a conversation a file starts was started.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public AgentConversationFiles(
        IAgentConversationStore conversations,
        IAgentConversationFileStore files,
        IStoredFileStore storedFiles,
        IAttachmentTextExtractor extractor,
        AgentConversationFilePassages passages,
        ActiveEmbeddingSpace space,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(storedFiles);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(passages);
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.conversations = conversations;
        this.files = files;
        this.storedFiles = storedFiles;
        this.extractor = extractor;
        this.passages = passages;
        this.space = space;
        this.timeProvider = timeProvider;
    }

    /// <summary>Takes one file into a conversation, starting the conversation where nothing stands under its identifier yet.</summary>
    /// <param name="conversation">The conversation, which the client named.</param>
    /// <param name="user">The person handing the file over, whose conversation it is or becomes.</param>
    /// <param name="name">The name the person's machine gave the file, or <see langword="null" /> where it gave none.</param>
    /// <param name="content">The whole file.</param>
    /// <param name="cancellationToken">Cancels the intake.</param>
    /// <returns>The file it became, or why it was not taken.</returns>
    /// <remarks>
    /// The empty, oversize, untaken-kind, and full-conversation refusals come before the file is read, so none of those
    /// files is parsed. Whose conversation it is decides the record step, after the read and the stored write: a
    /// conversation that is somebody else's, or one that cannot be started because the person already holds as many as
    /// they may, is refused there and the stored file removed. That costs a refused caller what taking a file into a
    /// conversation of their own would, and nothing more.
    /// </remarks>
    public async Task<AgentConversationFileIntake> TakeAsync(
        AgentConversationId conversation,
        UserId user,
        string? name,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        if (content.IsEmpty)
        {
            return AgentConversationFileIntake.Refused(AgentConversationFileRefusal.Empty);
        }

        if (content.Length > AgentConversationFileBounds.MaximumFileOctets)
        {
            return AgentConversationFileIntake.Refused(AgentConversationFileRefusal.TooLarge);
        }

        if (AgentConversationFileFormats.Recognize(content) is not { } recognized)
        {
            return AgentConversationFileIntake.Refused(AgentConversationFileRefusal.KindNotTaken);
        }

        await this.conversations.TryStartAsync(conversation, user, this.timeProvider.GetUtcNow(), cancellationToken);

        if ((await this.files.ListAsync(conversation, user, cancellationToken)).Count >= AgentConversationFileBounds.MaximumFilesPerConversation)
        {
            return AgentConversationFileIntake.Refused(AgentConversationFileRefusal.ConversationFull);
        }

        var read = recognized.Kind is AgentConversationFileKind.Document
            ? await this.ReadAsync(recognized.MediaType, content, cancellationToken)
            : null;

        if (await this.storedFiles.WriteAsync(user, recognized.MediaType, content, cancellationToken) is not { } stored)
        {
            return AgentConversationFileIntake.Refused(AgentConversationFileRefusal.NoSuchConversation);
        }

        var document = DocumentOf(AgentConversationFileId.Of(stored), NameOf(name), recognized, content.Length, this.timeProvider.GetUtcNow(), read);

        if (await this.files.RecordAsync(conversation, user, document, cancellationToken) is { } refusal)
        {
            await this.storedFiles.RemoveAsync(user, stored, cancellationToken);

            return AgentConversationFileIntake.Refused(refusal);
        }

        if (document.File.Reading is { IsLong: true })
        {
            await this.EmbedAsync(conversation, user, document, cancellationToken);
        }

        return AgentConversationFileIntake.Taken(document.File);
    }

    /// <summary>Lists the files one conversation holds, in the order they were taken in.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The files, and none where the conversation holds none or is not this person's.</returns>
    public Task<IReadOnlyList<AgentConversationFile>> ListAsync(
        AgentConversationId conversation,
        UserId user,
        CancellationToken cancellationToken) =>
        this.files.ListAsync(conversation, user, cancellationToken);

    /// <summary>Reads which of a conversation's files a question names, refusing a question that names one it does not hold or more than one question may carry.</summary>
    /// <param name="conversation">The conversation the question is asked in.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="named">The identifiers the question named, in the order it named them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The files, each once, or <see langword="null" /> where the question named too many, or one this conversation of this person does not hold.</returns>
    public async Task<IReadOnlyList<AgentConversationFileId>?> ResolveCarriedAsync(
        AgentConversationId conversation,
        UserId user,
        IReadOnlyList<Guid> named,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(named);

        Guid[] distinct = [.. named.Distinct()];

        if (distinct.Length is 0)
        {
            return [];
        }

        if (distinct.Length > AgentConversationFileBounds.MaximumFilesPerQuestion || distinct.Contains(Guid.Empty))
        {
            return null;
        }

        var held = (await this.files.ListAsync(conversation, user, cancellationToken)).Select(static file => file.Id.Value).ToHashSet();

        return distinct.All(held.Contains) ? [.. distinct.Select(AgentConversationFileId.Create)] : null;
    }

    /// <summary>Reads one document a conversation holds, with its words.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The file and its words, or <see langword="null" /> where this conversation of this person holds no such file.</returns>
    public Task<AgentConversationFileText?> ReadTextAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileId file,
        CancellationToken cancellationToken) =>
        this.files.ReadTextAsync(conversation, user, file, cancellationToken);

    /// <summary>Reads the octets of one picture a conversation holds.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="file">The picture.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The octets, or <see langword="null" /> where this conversation of this person holds no such picture or its octets are gone.</returns>
    public async Task<ReadOnlyMemory<byte>?> ReadPictureAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileId file,
        CancellationToken cancellationToken) =>
        await this.ReadContentAsync(conversation, user, file, cancellationToken) is { File.Kind: AgentConversationFileKind.Picture } picture
            ? (ReadOnlyMemory<byte>?)picture.Content
            : null;

    /// <summary>Reads one file a conversation holds, with its octets, which is what a download of it hands back.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The file and its octets, or <see langword="null" /> where this conversation of this person holds no such file or its octets are gone.</returns>
    /// <remarks>Read only once the conversation is known to hold it, so naming another conversation's file reaches nothing.</remarks>
    public async Task<AgentConversationFileContent?> ReadContentAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileId file,
        CancellationToken cancellationToken)
    {
        var held = (await this.files.ListAsync(conversation, user, cancellationToken)).FirstOrDefault(candidate => candidate.Id == file);

        return held is not null && await this.storedFiles.ReadAsync(user, file.StoredFile, cancellationToken) is { } content
            ? new AgentConversationFileContent(held, content)
            : null;
    }

    /// <summary>Searches one conversation's documents by the words of a query and by its meaning.</summary>
    /// <param name="conversation">The conversation, whose files are the only ones searched.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="query">What to look for.</param>
    /// <param name="limit">The most passages to return, at least one.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The passages found, the two searches' best taken in turn, and whether the search by meaning ran.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="query" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    public async Task<AgentConversationFileSearchResult> SearchAsync(
        AgentConversationId conversation,
        UserId user,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var documents = await this.files.ReadTextsAsync(conversation, user, cancellationToken);

        if (documents.Count is 0)
        {
            return new AgentConversationFileSearchResult([], SearchedByMeaning: false);
        }

        var byFile = documents.ToDictionary(static document => document.File.Id);
        var byWords = AgentConversationFileWordSearch.Rank(query, documents.Select(document => (document, this.passages.Cut(document))), limit);
        var placement = await this.space.PlaceAsync([query], cancellationToken);
        IReadOnlyList<AgentConversationFilePassage> byMeaning = placement is { Profile: { } profile, Vectors: [var vector] }
            ? await this.files.ReadNearestAsync(conversation, user, profile, vector, limit, cancellationToken)
            : [];

        var meaningFindings = byMeaning
            .Where(passage => byFile.ContainsKey(passage.File))
            .Select(passage => FindingOf(byFile[passage.File], passage, AgentConversationFileFoundBy.Meaning));
        var wordFindings = byWords.Select(found => FindingOf(byFile[found.File], found, AgentConversationFileFoundBy.Words));

        return new AgentConversationFileSearchResult(
            [.. Alternate(wordFindings, meaningFindings).DistinctBy(static finding => (finding.Passage.File, finding.Passage.StartOffset)).Take(limit)],
            placement.Profile is not null);
    }

    /// <summary>Deletes one conversation with every file it holds, the objects holding their octets included.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="user">Whose conversation it has to be.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns><see langword="true" /> when a conversation was removed; <see langword="false" /> where this person has no such conversation.</returns>
    /// <remarks>
    /// The conversation goes first, taking its file rows and every vector placed from them by cascade, so nothing can
    /// read a file of it after that; the stored files go next, each with the object that held it. A crash between the
    /// two leaves stored files nothing names, which the sweep of unlinked files removes.
    /// </remarks>
    public async Task<bool> DeleteConversationAsync(
        AgentConversationId conversation,
        UserId user,
        CancellationToken cancellationToken)
    {
        var held = await this.files.ListAsync(conversation, user, cancellationToken);

        if (!await this.conversations.TryDeleteAsync(conversation, user, cancellationToken))
        {
            return false;
        }

        foreach (var file in held)
        {
            await this.storedFiles.RemoveAsync(user, file.Id.StoredFile, cancellationToken);
        }

        return true;
    }

    private static string? NameOf(string? name) =>
        AttachmentFileName.TryNormalize(name, out var normalized) ? normalized.Value : null;

    private static AgentConversationFileText DocumentOf(
        AgentConversationFileId id,
        string? name,
        RecognizedConversationFile recognized,
        long byteLength,
        DateTimeOffset addedAt,
        AttachmentTextExtractionResult? read)
    {
        var text = read?.Text;
        var reading = read is null
            ? null
            : new AgentConversationFileReading(
                read.Outcome,
                text?.PageCount ?? 0,
                text?.Segments.Count > 0 ? text.Segments[0].Kind : null,
                text?.Text.Length ?? 0);

        return new AgentConversationFileText(
            new AgentConversationFile(id, name, recognized.Kind, recognized.MediaType, byteLength, addedAt, reading),
            text?.Text ?? string.Empty,
            text?.Segments ?? []);
    }

    private static AgentConversationFileFinding FindingOf(
        AgentConversationFileText document,
        AgentConversationFilePassage passage,
        AgentConversationFileFoundBy foundBy) =>
        new(
            passage with { Segment = AttachmentTextSegment.At(document.Segments, passage.StartOffset) },
            document.File.Name,
            document.Text[passage.StartOffset..Math.Min(passage.EndOffset, document.Text.Length)],
            foundBy);

    /// <summary>Takes the best of two rankings in turn, so neither search can push the other's best out of a short list.</summary>
    private static IEnumerable<AgentConversationFileFinding> Alternate(
        IEnumerable<AgentConversationFileFinding> first,
        IEnumerable<AgentConversationFileFinding> second)
    {
        using var left = first.GetEnumerator();
        using var right = second.GetEnumerator();
        var leftHas = left.MoveNext();
        var rightHas = right.MoveNext();

        while (leftHas || rightHas)
        {
            if (leftHas)
            {
                yield return left.Current;
                leftHas = left.MoveNext();
            }

            if (rightHas)
            {
                yield return right.Current;
                rightHas = right.MoveNext();
            }
        }
    }

    private async Task<AttachmentTextExtractionResult> ReadAsync(
        string mediaType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        await using var attachment = new HeldFileAttachment(mediaType, content);

        return await this.extractor.ExtractTextAsync(attachment, cancellationToken);
    }

    /// <summary>Places a long document's passages in the active space and records where each sits.</summary>
    private async Task EmbedAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileText document,
        CancellationToken cancellationToken)
    {
        var cut = this.passages.CutForEmbedding(document);

        if (cut.Count is 0)
        {
            return;
        }

        var placement = await this.space.PlaceAsync([.. cut.Select(passage => document.Text[passage.StartOffset..passage.EndOffset])], cancellationToken);

        if (placement is { Profile: { } profile, Vectors: { } vectors } && vectors.Count == cut.Count)
        {
            await this.files.SaveEmbeddingsAsync(conversation, user, profile, [.. cut.Zip(vectors)], cancellationToken);
        }
    }
}
