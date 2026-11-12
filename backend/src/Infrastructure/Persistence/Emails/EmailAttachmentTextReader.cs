// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads what each attachment reading of a message came to, and one reading's words with its page boundaries.</summary>
/// <remarks>
/// The outcomes are projected with the length of the words rather than the words, so a conversation reading that names
/// every file of twenty messages moves a handful of columns per attachment rather than up to the per-attachment ceiling
/// of each.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailAttachmentTextReader(MailFathomDbContext dbContext) : IEmailAttachmentTextReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<StoredEmailId, IReadOnlyList<AttachmentTextReading>>> ReadReadingsAsync(
        IReadOnlyList<StoredEmailId> emailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emailIds);

        if (emailIds.Count is 0)
        {
            return new Dictionary<StoredEmailId, IReadOnlyList<AttachmentTextReading>>();
        }

        Guid[] ids = [.. emailIds.Select(static emailId => emailId.Value)];

        var rows = await dbContext.EmailAttachmentTexts
            .AsNoTracking()
            .Where(text => ids.Contains(text.StoredEmailId))
            .OrderBy(text => text.StoredEmailId)
            .ThenBy(text => text.AttachmentPosition)
            .Select(text => new ReadingRow(
                text.StoredEmailId,
                text.AttachmentPosition,
                text.Kind,
                text.Outcome,
                text.PageCount,
                text.Segments,
                text.Text == null ? 0 : text.Text.Length))
            .ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(static row => StoredEmailId.Create(row.StoredEmailId))
            .ToDictionary(
                static message => message.Key,
                static message => (IReadOnlyList<AttachmentTextReading>)[.. message.Select(ReadingOf)]);
    }

    /// <inheritdoc />
    public async Task<AttachmentTextContent?> ReadTextAsync(
        StoredEmailId emailId,
        int attachmentPosition,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.EmailAttachmentTexts
            .AsNoTracking()
            .Where(text => text.StoredEmailId == emailId.Value && text.AttachmentPosition == attachmentPosition)
            .Select(text => new { text.Kind, text.Outcome, text.PageCount, text.Segments, text.Text })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var segments = DeserializeSegments(row.Segments);
        var words = row.Text ?? string.Empty;

        return new AttachmentTextContent(
            new AttachmentTextReading(attachmentPosition, row.Kind, row.Outcome, row.PageCount, PageKindOf(segments), words.Length),
            words,
            segments);
    }

    private static AttachmentTextReading ReadingOf(ReadingRow row) => new(
        row.AttachmentPosition,
        row.Kind,
        row.Outcome,
        row.PageCount,
        PageKindOf(DeserializeSegments(row.Segments)),
        row.TextLength);

    /// <summary>Names what the file was read into — pages, slides, or sheets — from the first boundary its reading recorded.</summary>
    private static AttachmentTextSegmentKind? PageKindOf(IReadOnlyList<AttachmentTextSegment> segments) =>
        segments.Count is 0 ? null : segments[0].Kind;

    /// <summary>Reads back the boundary document one attachment's row stores, which a row written before boundaries existed lacks.</summary>
    private static IReadOnlyList<AttachmentTextSegment> DeserializeSegments(string? document) => document is null
        ? []
        : JsonSerializer.Deserialize(
            document,
            AttachmentTextSegmentJsonContext.Default.IReadOnlyListAttachmentTextSegment) ?? [];

    /// <summary>One attachment's reading as the projection returns it, with the length of its words in place of them.</summary>
    private sealed record ReadingRow(
        Guid StoredEmailId,
        int AttachmentPosition,
        AttachmentTextKind Kind,
        string Outcome,
        int PageCount,
        string? Segments,
        int TextLength);
}
