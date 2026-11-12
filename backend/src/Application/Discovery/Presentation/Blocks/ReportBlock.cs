// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Files;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>One report the Agent wrote, handed to the person in its answer as the PDF the service set it as.</summary>
/// <remarks>
/// <para>
/// The block for "write it up so I can forward it". It names the file rather than carrying it: the report is a file of
/// the conversation the answer stands in, stored, classified, and deleted on the terms every file of a conversation is,
/// and a download is that conversation's own file route, held to the person's grant at the moment they ask — a
/// conversation deleted since answers as no file at all.
/// </para>
/// <para>
/// The name, the size, and the page count are what the file was when it was written, so a reader can tell which report
/// it is before opening it. The evidence cites the messages the report's list of sources names, as many of them as a
/// block's evidence holds.
/// </para>
/// <para>
/// It is a reading rather than a proposal. Writing a report changes nothing in the mailbox, so there is nothing for the
/// person to accept, and the block is written on the run's own turn.
/// </para>
/// </remarks>
public sealed record ReportBlock : PresentationBlock
{
    /// <summary>Initializes one report handed to the person.</summary>
    /// <param name="evidence">What the correspondence does for the report, which cites the messages it rests on.</param>
    /// <param name="fileId">The conversation's file the report was stored as.</param>
    /// <param name="title">The report's title.</param>
    /// <param name="name">The name the file is downloaded under.</param>
    /// <param name="mediaType">What the file is, which is a PDF.</param>
    /// <param name="sizeOctets">How large the file is.</param>
    /// <param name="pageCount">How many pages it was set on.</param>
    /// <param name="writtenAt">When it was written, which its first page states.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the file, the title, the name, or the media type is the unspecified default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="sizeOctets" /> is negative or <paramref name="pageCount" /> is below one.</exception>
    public ReportBlock(
        PresentationEvidence evidence,
        AgentConversationFileId fileId,
        PresentationText title,
        PresentationText name,
        PresentationText mediaType,
        long sizeOctets,
        int pageCount,
        DateTimeOffset writtenAt)
        : base(PresentationBlockType.Report, evidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeOctets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        PresentationRequirement.Specified(title, nameof(title));
        PresentationRequirement.Specified(name, nameof(name));
        PresentationRequirement.Specified(mediaType, nameof(mediaType));

        if (fileId.Value == Guid.Empty)
        {
            throw new ArgumentException("A report names the conversation's file it was stored as.", nameof(fileId));
        }

        this.FileId = fileId;
        this.Title = title;
        this.Name = name;
        this.MediaType = mediaType;
        this.SizeOctets = sizeOctets;
        this.PageCount = pageCount;
        this.WrittenAt = writtenAt;
    }

    /// <summary>Gets the conversation's file the report was stored as, which its download names.</summary>
    public AgentConversationFileId FileId { get; }

    /// <summary>Gets the report's title.</summary>
    public PresentationText Title { get; }

    /// <summary>Gets the name the file is downloaded under.</summary>
    public PresentationText Name { get; }

    /// <summary>Gets what the file is, which is a PDF.</summary>
    public PresentationText MediaType { get; }

    /// <summary>Gets how large the file is.</summary>
    public long SizeOctets { get; }

    /// <summary>Gets how many pages it was set on.</summary>
    public int PageCount { get; }

    /// <summary>Gets when it was written, which its first page states.</summary>
    public DateTimeOffset WrittenAt { get; }
}
