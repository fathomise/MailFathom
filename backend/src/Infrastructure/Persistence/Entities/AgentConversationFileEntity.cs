// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One file a person handed to one of their conversations with the Agent, and what reading it came to.</summary>
/// <remarks>
/// <para>
/// Keyed by the stored file holding the octets and hanging off both that file and the conversation, so it goes with
/// whichever goes first: a conversation deleted takes it, and a person erased takes it through both. The octets are not
/// here, which is what leaves the move and the release of stored content reaching a conversation's file exactly as they
/// reach every other stored file.
/// </para>
/// <para>
/// The words read out of a document are kept beside it, in the clear, on the terms mail's own derived text is kept on:
/// they are what a run reads and searches, and reading them again from the octets on every turn would put a parser in
/// front of every answer.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class AgentConversationFileEntity
{
    /// <summary>The table these rows live in, named here because statements against it are composed.</summary>
    internal const string TableName = "agent_conversation_files";

    /// <summary>The longest media type a file is recorded under.</summary>
    internal const int MaximumMediaTypeLength = StoredFileEntity.MaximumMediaTypeLength;

    /// <summary>Gets or sets the identifier, which is the stored file's.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the conversation holding the file.</summary>
    public Guid ConversationId { get; set; }

    /// <summary>Gets or sets the name the person's machine gave the file, normalized, or <see langword="null" /> where it gave none usable.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets what the octets proved the file to be.</summary>
    public required string Kind { get; set; }

    /// <summary>Gets or sets the media type the octets proved.</summary>
    public required string MediaType { get; set; }

    /// <summary>Gets or sets how many octets the file holds.</summary>
    public long ByteLength { get; set; }

    /// <summary>Gets or sets when the file was taken in, in UTC.</summary>
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>Gets or sets what the attachment text reader said of a document, or <see langword="null" /> for a picture.</summary>
    public string? TextOutcome { get; set; }

    /// <summary>Gets or sets how many pages, slides, or sheets a document's words were read into.</summary>
    public int PageCount { get; set; }

    /// <summary>Gets or sets what those places are, or <see langword="null" /> where nothing marked any.</summary>
    public string? PageKind { get; set; }

    /// <summary>Gets or sets a document's words, and empty for a picture or a document nothing could read.</summary>
    public required string Text { get; set; }

    /// <summary>Gets or sets where each page, slide, or sheet begins in <see cref="Text" />, as the document attachment readings store, or <see langword="null" /> where nothing marked any.</summary>
    public string? Segments { get; set; }
}
