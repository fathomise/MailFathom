// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>Reads back what reading each attachment of a message produced: the outcome always, and the words where a reader asks for one file's.</summary>
/// <remarks>
/// <para>
/// Two reads because they serve two questions of different weight. What every file of a conversation came to — read
/// into fourteen pages, refused as an unsupported format, never offered a parser — is a few numbers and a word per
/// attachment, and a reader shown a conversation is owed it for every message so that a file nobody could read is
/// named rather than passed over. The words themselves are up to the per-attachment ceiling the reading applied, so
/// they are read for one file at a time, when somebody reads that file.
/// </para>
/// <para>
/// Nothing here checks that the caller may read the message. Every caller reaches a message through the content read
/// first, under the reader's own scope, and names a file only of a message that read served — which is what keeps this
/// port from being a second way into mail the scope excludes.
/// </para>
/// <para>
/// It reaches no parser, no provider, and no mail server: everything it returns was derived by a background pass and
/// stored, which is what lets a read path use it at all.
/// </para>
/// </remarks>
public interface IEmailAttachmentTextReader
{
    /// <summary>Reads what reading each attachment of the given messages came to, without any of the words.</summary>
    /// <param name="emailIds">The messages, at most a conversation reading's worth.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Each message's readings by attachment position; a message nothing has read an attachment of is absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="emailIds" /> is <see langword="null" />.</exception>
    Task<IReadOnlyDictionary<StoredEmailId, IReadOnlyList<AttachmentTextReading>>> ReadReadingsAsync(
        IReadOnlyList<StoredEmailId> emailIds,
        CancellationToken cancellationToken);

    /// <summary>Reads the words one attachment yielded, with where each of its pages begins in them.</summary>
    /// <param name="emailId">The message carrying the attachment.</param>
    /// <param name="attachmentPosition">The attachment's zero-based walk position.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reading and its words, the words empty where it yielded none; or <see langword="null" /> where nothing has read that attachment.</returns>
    Task<AttachmentTextContent?> ReadTextAsync(
        StoredEmailId emailId,
        int attachmentPosition,
        CancellationToken cancellationToken);
}
