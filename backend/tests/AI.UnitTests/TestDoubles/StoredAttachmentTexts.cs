// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Domain.Emails;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Holds the readings a background pass would have stored for each attachment, answering both reads from them.</summary>
internal sealed class StoredAttachmentTexts : IEmailAttachmentTextReader
{
    private readonly Dictionary<(StoredEmailId Email, int Position), AttachmentTextContent> stored = [];

    /// <summary>Gets a store holding nothing, which is what a deployment reading no attachment answers.</summary>
    public static StoredAttachmentTexts None => new();

    /// <summary>Records what one attachment's reading came to.</summary>
    /// <param name="email">The message carrying it.</param>
    /// <param name="content">The reading and its words.</param>
    /// <returns>This store, so arrangement reads as one statement.</returns>
    public StoredAttachmentTexts Holding(StoredEmailId email, AttachmentTextContent content)
    {
        this.stored[(email, content.Reading.AttachmentPosition)] = content;

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<StoredEmailId, IReadOnlyList<AttachmentTextReading>>> ReadReadingsAsync(
        IReadOnlyList<StoredEmailId> emailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emailIds);

        IReadOnlyDictionary<StoredEmailId, IReadOnlyList<AttachmentTextReading>> readings = this.stored
            .Where(entry => emailIds.Contains(entry.Key.Email))
            .GroupBy(static entry => entry.Key.Email)
            .ToDictionary(
                static message => message.Key,
                static message => (IReadOnlyList<AttachmentTextReading>)[.. message.OrderBy(static entry => entry.Key.Position).Select(static entry => entry.Value.Reading)]);

        return Task.FromResult(readings);
    }

    /// <inheritdoc />
    public Task<AttachmentTextContent?> ReadTextAsync(
        StoredEmailId emailId,
        int attachmentPosition,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.stored.GetValueOrDefault((emailId, attachmentPosition)));
}
