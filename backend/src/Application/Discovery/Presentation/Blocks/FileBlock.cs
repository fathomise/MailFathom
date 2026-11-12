// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>One file handed to the person in an answer, to download whole: a file a message they may read carries.</summary>
/// <remarks>
/// <para>
/// The block for "send me the lease Clara sent". It names the file rather than carrying it: the message the file
/// arrived on, through the citation the answer declared for that message, and the file's position among that
/// message's attachments. Nothing is copied, so the octets stay where the mail is and a download is the mail screen's
/// own, held to the reader's grant at the moment they ask for it — a message removed or withheld since the answer was
/// written answers as no file at all, exactly as it does on the mail screen.
/// </para>
/// <para>
/// The name, the media type, the size, the sender, and the date are what the message said about the file when the
/// answer was written, so a reader can tell which file it is before deciding to have it. The media type is what the
/// message declared rather than what the content is: a client draws its kind from it and never decides how to open
/// anything by it.
/// </para>
/// <para>
/// It is a reading rather than a proposal. Downloading changes nothing in the mailbox, so there is nothing for the
/// person to accept, and the block is written on the run's own turn.
/// </para>
/// </remarks>
public sealed record FileBlock : PresentationBlock
{
    /// <summary>Initializes one file handed to the person.</summary>
    /// <param name="evidence">What the correspondence does for the file, which cites the message it arrived on.</param>
    /// <param name="source">The citation resolving to the message the file arrived on.</param>
    /// <param name="attachmentPosition">The file's place in the order the message's attachments are read in, counted from zero.</param>
    /// <param name="name">The name the sender gave the file, or <see langword="null" /> where they gave it none.</param>
    /// <param name="mediaType">The media type the message declared for the file.</param>
    /// <param name="sizeOctets">How large the message says the file is.</param>
    /// <param name="sender">Who sent the message, by the name its sender header carries or else its address; <see langword="null" /> where the message names nobody.</param>
    /// <param name="sentAt">When the message says it was sent, or <see langword="null" /> where it says nothing.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the source, the media type, a stated name, or a stated sender is the unspecified default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="attachmentPosition" /> or <paramref name="sizeOctets" /> is negative.</exception>
    public FileBlock(
        PresentationEvidence evidence,
        PresentationCitationId source,
        int attachmentPosition,
        PresentationText? name,
        PresentationText mediaType,
        long sizeOctets,
        PresentationText? sender,
        DateTimeOffset? sentAt)
        : base(PresentationBlockType.File, evidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attachmentPosition);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeOctets);
        PresentationRequirement.Specified(mediaType, nameof(mediaType));

        if (name is { } named)
        {
            PresentationRequirement.Specified(named, nameof(name));
        }

        if (sender is { } stated)
        {
            PresentationRequirement.Specified(stated, nameof(sender));
        }

        if (!source.IsSpecified)
        {
            throw new ArgumentException("A file names the citation of the message it arrived on.", nameof(source));
        }

        this.Source = source;
        this.AttachmentPosition = attachmentPosition;
        this.Name = name;
        this.MediaType = mediaType;
        this.SizeOctets = sizeOctets;
        this.Sender = sender;
        this.SentAt = sentAt;
    }

    /// <summary>Gets the citation resolving to the message the file arrived on.</summary>
    public PresentationCitationId Source { get; }

    /// <summary>Gets the file's place in the order the message's attachments are read in, counted from zero.</summary>
    public int AttachmentPosition { get; }

    /// <summary>Gets the name the sender gave the file, or <see langword="null" /> where they gave it none.</summary>
    public PresentationText? Name { get; }

    /// <summary>Gets the media type the message declared for the file.</summary>
    public PresentationText MediaType { get; }

    /// <summary>Gets how large the message says the file is.</summary>
    public long SizeOctets { get; }

    /// <summary>Gets who sent the message the file arrived on, or <see langword="null" /> where it names nobody.</summary>
    public PresentationText? Sender { get; }

    /// <summary>Gets when the message the file arrived on says it was sent, or <see langword="null" /> where it says nothing.</summary>
    public DateTimeOffset? SentAt { get; }

    /// <inheritdoc />
    public override IEnumerable<PresentationCitationId> ReferencedCitations =>
        base.ReferencedCitations.Append(this.Source);
}
