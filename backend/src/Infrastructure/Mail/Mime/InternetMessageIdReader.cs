// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MimeKit;
using MimeKit.Utils;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reads the <c>Message-ID</c> a message carries, in the one form a send's record and a signed arrival are compared in.</summary>
/// <remarks>
/// A send's record keeps the identifier of the MIME it was enqueued with, and a receiving account of the same
/// organization later looks that record up by the identifier its arrival names. Both sides read it here so they cannot
/// disagree about angle brackets, comments, or whitespace, and an identifier past the stored bound is treated as none —
/// which can only ever refuse a lookup, never grant one.
/// </remarks>
internal static class InternetMessageIdReader
{
    /// <summary>The longest identifier read, matching the bound every column holding one carries.</summary>
    internal const int MaximumLength = 998;

    /// <summary>Reads the identifier out of a message's header block alone.</summary>
    /// <param name="rawMime">The message's bytes.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The identifier, or <see langword="null" /> where the message carries no usable one.</returns>
    public static async Task<string?> ReadAsync(ReadOnlyMemory<byte> rawMime, CancellationToken cancellationToken)
    {
        await using var stream = RawMimeStream.Open(rawMime);

        try
        {
            return Read(await HeaderList.LoadAsync(stream, cancellationToken));
        }
        catch (FormatException)
        {
            // A header block MimeKit cannot read names no identifier; the send itself is not this reading's to refuse.
            return null;
        }
    }

    /// <summary>Reads the identifier out of headers already parsed.</summary>
    /// <param name="headers">The message's headers.</param>
    /// <returns>The identifier, or <see langword="null" /> where the message carries no usable one.</returns>
    public static string? Read(HeaderList headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return headers[HeaderId.MessageId] is { } written
            && MimeUtils.ParseMessageId(written)?.Trim() is { Length: > 0 and <= MaximumLength } identifier
                ? identifier
                : null;
    }
}
