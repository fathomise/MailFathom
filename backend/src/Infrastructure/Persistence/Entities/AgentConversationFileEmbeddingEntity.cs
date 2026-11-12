// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using Pgvector;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>Where one passage of a document a conversation holds sits in one space.</summary>
/// <remarks>
/// It hangs off the file it was placed from, so it goes with the file — with the conversation, and with the person — and
/// inherits everything the file is: classification, retention, and who may read it. A conversation's vectors are read
/// for that conversation alone.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class AgentConversationFileEmbeddingEntity
{
    /// <summary>The table these rows live in, named here because statements against it are composed.</summary>
    internal const string TableName = "agent_conversation_file_embeddings";

    /// <summary>Gets or sets the file the passage stands in.</summary>
    public Guid FileId { get; set; }

    /// <summary>Gets or sets the passage's place among the file's passages.</summary>
    public int Ordinal { get; set; }

    /// <summary>Gets or sets the space the vector was placed in.</summary>
    public Guid EmbeddingProfileId { get; set; }

    /// <summary>Gets or sets how many components the vector has, which the space declares and the check enforces.</summary>
    public int Dimension { get; set; }

    /// <summary>Gets or sets the vector itself.</summary>
    public required Vector Embedding { get; set; }

    /// <summary>Gets or sets where the passage begins in the file's words.</summary>
    public int StartOffset { get; set; }

    /// <summary>Gets or sets where the passage ends in the file's words, exclusive.</summary>
    public int EndOffset { get; set; }

    /// <summary>Gets or sets when the vector was recorded, in UTC.</summary>
    public DateTimeOffset GeneratedAt { get; set; }
}
