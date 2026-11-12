// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace MailFathom.Infrastructure.Persistence.Agent;

/// <summary>Keeps which files each conversation holds in <c>agent_conversation_files</c>, and their passages' vectors beside them.</summary>
/// <remarks>
/// <para>
/// Every read joins the conversation's row and names its person, so a file reads as absent to anybody but the person
/// whose conversation holds it, and to that person from any other conversation.
/// </para>
/// <para>
/// Nothing logs. A file's name and words are something one identified person handed over.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AgentConversationFileStore(
    MailFathomDbContext dbContext,
    TimeProvider timeProvider) : IAgentConversationFileStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The conversation's row is locked in a statement of its own and the files are counted in the next, inside one
    /// transaction: under read committed a statement that waited for a lock still counts from the snapshot it started
    /// with, so a count taken beside the lock would let two uploads at once both take the last place. Where nothing is
    /// written, the conversation is full or is not this person's; the stored file the row would have named is the
    /// caller's to remove.
    /// </remarks>
    public async Task<AgentConversationFileRefusal?> RecordAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileText document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var file = document.File;
        var reading = file.Reading;
        var segments = document.Segments.Count is 0
            ? null
            : JsonSerializer.Serialize(document.Segments, AttachmentTextSegmentJsonContext.Default.IReadOnlyListAttachmentTextSegment);
        var conversationId = conversation.Value;
        var userId = user.Value;

        await using var recording = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var locked = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                 SELECT c."Id" AS "Value" FROM agent_conversations AS c
                 WHERE c."Id" = {conversationId} AND c."UserId" = {userId}
                 FOR UPDATE
                 """)
            .ToListAsync(cancellationToken);

        if (locked.Count is 0)
        {
            return AgentConversationFileRefusal.NoSuchConversation;
        }

        int written;
        try
        {
            written = await dbContext.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO agent_conversation_files
                     ("Id", "ConversationId", "Name", "Kind", "MediaType", "ByteLength", "AddedAt", "TextOutcome", "PageCount", "PageKind", "Text", "Segments")
                 SELECT {file.Id.Value}, {conversationId}, CAST({file.Name} AS varchar), {file.Kind.ToString()}, {file.MediaType}, {file.ByteLength},
                        {file.AddedAt}, CAST({reading?.Outcome.ToString()} AS varchar), {reading?.PageCount ?? 0}, CAST({reading?.PageKind?.ToString()} AS varchar),
                        {document.Text}, CAST(CAST({segments} AS text) AS jsonb)
                 WHERE (SELECT count(*) FROM agent_conversation_files AS f WHERE f."ConversationId" = {conversationId})
                     < {AgentConversationFileBounds.MaximumFilesPerConversation}
                 """,
                cancellationToken);
        }
        catch (PostgresException refused) when (refused.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // The stored file was removed, or the person erased, between its write and this one.
            return AgentConversationFileRefusal.NoSuchConversation;
        }

        if (written is 0)
        {
            return AgentConversationFileRefusal.ConversationFull;
        }

        await recording.CommitAsync(cancellationToken);

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentConversationFile>> ListAsync(
        AgentConversationId conversation,
        UserId user,
        CancellationToken cancellationToken)
    {
        var rows = await this.Held(conversation, user)
            .OrderBy(static file => file.AddedAt)
            .ThenBy(static file => file.Id)
            .Select(static file => new FileRow(
                file.Id,
                file.Name,
                file.Kind,
                file.MediaType,
                file.ByteLength,
                file.AddedAt,
                file.TextOutcome,
                file.PageCount,
                file.PageKind,
                file.Text.Length))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(FileOf)];
    }

    /// <inheritdoc />
    public async Task<AgentConversationFileText?> ReadTextAsync(
        AgentConversationId conversation,
        UserId user,
        AgentConversationFileId file,
        CancellationToken cancellationToken)
    {
        var fileId = file.Value;
        var row = await this.Held(conversation, user)
            .Where(held => held.Id == fileId)
            .Select(static held => new TextRow(
                new FileRow(held.Id, held.Name, held.Kind, held.MediaType, held.ByteLength, held.AddedAt, held.TextOutcome, held.PageCount, held.PageKind, held.Text.Length),
                held.Text,
                held.Segments))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : TextOf(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentConversationFileText>> ReadTextsAsync(
        AgentConversationId conversation,
        UserId user,
        CancellationToken cancellationToken)
    {
        var rows = await this.Held(conversation, user)
            .Where(static held => held.Text.Length > 0)
            .OrderBy(static held => held.AddedAt)
            .ThenBy(static held => held.Id)
            .Select(static held => new TextRow(
                new FileRow(held.Id, held.Name, held.Kind, held.MediaType, held.ByteLength, held.AddedAt, held.TextOutcome, held.PageCount, held.PageKind, held.Text.Length),
                held.Text,
                held.Segments))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(TextOf)];
    }

    /// <inheritdoc />
    public async Task SaveEmbeddingsAsync(
        AgentConversationId conversation,
        UserId user,
        RegisteredEmbeddingProfile profile,
        IReadOnlyList<(AgentConversationFilePassage Passage, EmbeddingVector Vector)> passages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(passages);

        Guid[] files = [.. passages.Select(static placed => placed.Passage.File.Value).Distinct()];
        var held = await this.Held(conversation, user)
            .Where(file => files.Contains(file.Id))
            .Select(static file => file.Id)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        dbContext.AgentConversationFileEmbeddings.AddRange(passages
            .Where(placed => held.Contains(placed.Passage.File.Value))
            .Select(placed => new AgentConversationFileEmbeddingEntity
            {
                FileId = placed.Passage.File.Value,
                Ordinal = placed.Passage.Ordinal,
                EmbeddingProfileId = profile.Id.Value,
                Dimension = profile.Identity.Dimension,
                Embedding = new Vector(placed.Vector.Components),
                StartOffset = placed.Passage.StartOffset,
                EndOffset = placed.Passage.EndOffset,
                GeneratedAt = now,
            }));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException refused) when (refused.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The conversation was deleted while its file was being embedded, which leaves vectors with nothing to hang
            // off rather than a fault: the person removed what they were placed from.
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentConversationFilePassage>> ReadNearestAsync(
        AgentConversationId conversation,
        UserId user,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var rows = await dbContext.Database
            .SqlQueryRaw<NearestRow>(
                NearestStatement(profile.Identity.DistanceMetric),
                new NpgsqlParameter("target", new Vector(queryVector.Components)),
                new NpgsqlParameter("profileId", profile.Id.Value),
                new NpgsqlParameter("conversationId", conversation.Value),
                new NpgsqlParameter("userId", user.Value),
                new NpgsqlParameter("limit", limit))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(static row => new AgentConversationFilePassage(AgentConversationFileId.Create(row.FileId), row.Ordinal, row.StartOffset, row.EndOffset, Segment: null))];
    }

    /// <summary>Composes the nearest-passages read under the operator a space's metric ranks by.</summary>
    private static string NearestStatement(EmbeddingDistanceMetric metric)
    {
        var distanceOperator = metric switch
        {
            EmbeddingDistanceMetric.Cosine => "<=>",
            EmbeddingDistanceMetric.InnerProduct => "<#>",
            EmbeddingDistanceMetric.EuclideanDistance => "<->",
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "A space declares a known metric."),
        };

        return $"""
            SELECT v."FileId", v."Ordinal", v."StartOffset", v."EndOffset"
            FROM agent_conversation_file_embeddings AS v
            JOIN agent_conversation_files AS f ON f."Id" = v."FileId"
            JOIN agent_conversations AS c ON c."Id" = f."ConversationId"
            WHERE f."ConversationId" = @conversationId
              AND c."UserId" = @userId
              AND v."EmbeddingProfileId" = @profileId
            ORDER BY v."Embedding" {distanceOperator} @target, v."FileId", v."Ordinal"
            LIMIT @limit
            """;
    }

    private static AgentConversationFile FileOf(FileRow row) => new(
        AgentConversationFileId.Create(row.Id),
        row.Name,
        Enum.Parse<AgentConversationFileKind>(row.Kind),
        row.MediaType,
        row.ByteLength,
        row.AddedAt,
        row.TextOutcome is { } outcome
            ? new AgentConversationFileReading(
                Enum.Parse<AttachmentTextExtractionOutcome>(outcome),
                row.PageCount,
                row.PageKind is { } kind ? Enum.Parse<AttachmentTextSegmentKind>(kind) : null,
                row.TextLength)
            : null);

    private static AgentConversationFileText TextOf(TextRow row) => new(
        FileOf(row.File),
        row.Text,
        row.Segments is null
            ? []
            : JsonSerializer.Deserialize(row.Segments, AttachmentTextSegmentJsonContext.Default.IReadOnlyListAttachmentTextSegment) ?? []);

    private IQueryable<AgentConversationFileEntity> Held(AgentConversationId conversation, UserId user)
    {
        var conversationId = conversation.Value;
        var userId = user.Value;

        return dbContext.AgentConversationFiles
            .AsNoTracking()
            .Where(file => file.ConversationId == conversationId
                && dbContext.AgentConversations.Any(held => held.Id == conversationId && held.UserId == userId));
    }

    /// <summary>One file as the listing projects it, its words left out but their length.</summary>
    private sealed record FileRow(
        Guid Id,
        string? Name,
        string Kind,
        string MediaType,
        long ByteLength,
        DateTimeOffset AddedAt,
        string? TextOutcome,
        int PageCount,
        string? PageKind,
        int TextLength);

    /// <summary>One document with its words, as the reading projects it.</summary>
    private sealed record TextRow(FileRow File, string Text, string? Segments);

    /// <summary>One passage as the nearest read answers it.</summary>
    private sealed class NearestRow
    {
        public Guid FileId { get; init; }

        public int Ordinal { get; init; }

        public int StartOffset { get; init; }

        public int EndOffset { get; init; }
    }
}
