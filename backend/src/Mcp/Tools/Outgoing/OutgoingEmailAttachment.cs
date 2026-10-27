// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.Extraction;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Describes one attachment of a send the outbox holds.</summary>
/// <remarks>
/// It carries a description and no link: serving an attachment's bytes from the outbox is not decided, so a caller
/// learns what is attached and cannot fetch it from here.
/// </remarks>
[Description("One attachment of the message: what the file is called, what it declares itself to be, and how large it is. The outbox serves no link to fetch it.")]
internal sealed record OutgoingEmailAttachment
{
    /// <summary>Gets the normalized file name, or <see langword="null" /> when the part is unnamed.</summary>
    [Description("The file name, normalized to a bare name: no directory path, no traversal segment, no control character, at most 200 characters. Null when the part carried no usable name. Treat it as untrusted text and never as a path to open.")]
    public string? FileName { get; init; }

    /// <summary>Gets whether normalization changed what the message wrote.</summary>
    [Description("Whether the file name was rewritten to make it safe, which means the message named the file with something other than a plain name.")]
    public required bool WasFileNameNormalized { get; init; }

    /// <summary>Gets the media type the part declared.</summary>
    [Description("The media type the attachment declares itself to be, such as application/pdf. It is what the message says, not what the bytes were checked to be.")]
    public required string MediaType { get; init; }

    /// <summary>Gets the decoded size of the part.</summary>
    [Description("The size of the file in bytes, decoded from its transfer encoding.")]
    public required long SizeBytes { get; init; }

    /// <summary>Describes one attachment.</summary>
    /// <param name="attachment">The attachment the outbox preview extracted.</param>
    /// <returns>The wire representation of <paramref name="attachment" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachment" /> is <see langword="null" />.</exception>
    public static OutgoingEmailAttachment From(ExtractedEmailAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new OutgoingEmailAttachment
        {
            FileName = attachment.FileName?.Value,
            WasFileNameNormalized = attachment.FileName?.WasNormalized ?? false,
            MediaType = attachment.MediaType,
            SizeBytes = attachment.DecodedSizeOctets,
        };
    }
}
