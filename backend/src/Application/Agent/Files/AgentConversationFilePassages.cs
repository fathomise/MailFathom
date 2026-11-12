// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Agent.Files;

/// <summary>Cuts a document a conversation holds into the passages it is searched and embedded by.</summary>
/// <remarks>
/// <para>
/// The cut is the one mail and its attachments are cut by, under the same rules, so a passage out of a person's own file
/// sits in the active vector space exactly as comparable to a question as a passage out of their mail does. It is made
/// one page, slide, or sheet at a time, so no passage straddles two of them and each keeps the place it came from.
/// </para>
/// <para>
/// Deterministic: the same words and the same rules give the same passages, which is what lets the search by words cut
/// them again on every run while the vectors recorded at intake keep naming the same spans.
/// </para>
/// <para>
/// What is embedded is cut under the deployment's per-message input bound, spent once across every page of the file, so
/// a file costs a provider no more than one message's text does; the search by words cuts the whole text, which costs
/// no provider anything and is already bounded by the characters the reader takes out of a file.
/// </para>
/// </remarks>
public sealed class AgentConversationFilePassages
{
    private readonly IEmailTextChunker chunker;
    private readonly EmailChunkingRules rules;
    private readonly EmbeddingInputBound embeddingBound;

    /// <summary>Initializes the cut.</summary>
    /// <param name="chunker">Cuts text the way mail is cut.</param>
    /// <param name="rules">The boundaries mail is cut along.</param>
    /// <param name="embeddingBound">The characters of one message's text a deployment embeds, which bound one file's too.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public AgentConversationFilePassages(IEmailTextChunker chunker, EmailChunkingRules rules, EmbeddingInputBound embeddingBound)
    {
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(embeddingBound);

        this.chunker = chunker;
        this.rules = rules;
        this.embeddingBound = embeddingBound;
    }

    /// <summary>Cuts one document into passages, in reading order.</summary>
    /// <param name="document">The document and its words.</param>
    /// <returns>Its passages, and none where nothing was read out of it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public IReadOnlyList<AgentConversationFilePassage> Cut(AgentConversationFileText document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return this.Cut(document, allowance: document.Text.Length);
    }

    /// <summary>Cuts the passages of one document that are embedded, under one allowance spent across all its pages.</summary>
    /// <param name="document">The document and its words.</param>
    /// <returns>Its passages up to the deployment's per-message input bound, in reading order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One allowance for the whole file rather than one per page, for the reason the mail path spends one per message:
    /// handing the bound to each page would make it a per-page limit, and a file of many pages would be embedded to many
    /// times what the deployment says one item may cost.
    /// </remarks>
    public IReadOnlyList<AgentConversationFilePassage> CutForEmbedding(AgentConversationFileText document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return this.Cut(document, this.embeddingBound.MaximumCharacterCount);
    }

    private List<AgentConversationFilePassage> Cut(AgentConversationFileText document, int allowance)
    {
        if (document.Text.Length is 0)
        {
            return [];
        }

        var source = EmailChunkAttachmentSource.Create(0, document.File.MediaType);
        var ordinal = 0;
        var remaining = allowance;
        List<AgentConversationFilePassage> passages = [];

        foreach (var (start, end, segment) in PlacesOf(document))
        {
            if (remaining <= 0)
            {
                break;
            }

            var cut = this.chunker.DeriveAttachmentChunks(
                document.Text[start..end],
                this.rules,
                EmbeddingInputBound.Create(Math.Min(Math.Max(1, end - start), remaining)),
                source);

            remaining -= end - start;

            passages.AddRange(cut.Chunks.Select(chunk => new AgentConversationFilePassage(
                document.File.Id,
                ordinal++,
                start + chunk.StartOffset,
                start + chunk.EndOffset,
                segment)));
        }

        return passages;
    }

    /// <summary>Reads where each page, slide, or sheet begins and ends, or the whole text as one place where nothing marked any.</summary>
    private static IEnumerable<(int Start, int End, AttachmentTextSegment? Segment)> PlacesOf(AgentConversationFileText document)
    {
        var segments = document.Segments
            .Where(segment => segment.StartOffset >= 0 && segment.StartOffset < document.Text.Length)
            .OrderBy(static segment => segment.StartOffset)
            .ToArray();

        if (segments.Length is 0 || segments[0].StartOffset > 0)
        {
            var firstEnd = segments.Length is 0 ? document.Text.Length : segments[0].StartOffset;
            yield return (0, firstEnd, null);
        }

        foreach (var (segment, index) in segments.Select(static (segment, index) => (segment, index)))
        {
            var end = index + 1 < segments.Length ? segments[index + 1].StartOffset : document.Text.Length;

            if (end > segment.StartOffset)
            {
                yield return (segment.StartOffset, end, segment);
            }
        }
    }
}
