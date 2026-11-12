// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.Extraction;

namespace MailFathom.Application.Agent.Files;

/// <summary>Hands a file a person uploaded to the attachment text reader as the attachment it reads.</summary>
/// <param name="mediaType">The media type the octets proved, which is the only thing the reader chooses its parser by.</param>
/// <param name="content">The octets, which this holds without owning.</param>
/// <remarks>
/// The name is left out on purpose. The reader falls back to a name's extension where a type is unknown, and a
/// conversation's file is read as what its octets are, never as what it was called.
/// </remarks>
internal sealed class HeldFileAttachment(string mediaType, ReadOnlyMemory<byte> content) : IOpenedEmailAttachment
{
    /// <inheritdoc />
    public ExtractedEmailAttachment Description { get; } = new(FileName: null, mediaType, content.Length);

    /// <inheritdoc />
    public Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return destination.WriteAsync(content, cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
