// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Mail.Mime;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Builds the in-memory raw MIME the extraction tests parse.</summary>
/// <remarks>
/// Every fixture is written as text and turned into bytes here, so no test touches the file system and the exact header
/// each rule is about stays readable in the test that asserts on it.
/// </remarks>
internal static class MimeFixtures
{
    /// <summary>Gets the occurrence identity every fixture is fetched under.</summary>
    public static EmailOccurrenceId OccurrenceId { get; } = EmailOccurrenceId.Create(
        MailAccountId.Create("primary"),
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("inbox"), RemoteFolderPath.Create("INBOX", '/')).Id,
        ImapUidValidity.Create(5),
        ImapUid.Create(10));

    /// <summary>Gets the account every fixture is read for, which is the account its occurrence identity names.</summary>
    public static MailAccountId Account { get; } = MailAccountId.Create("primary");

    /// <summary>Turns MIME lines into the fetched content an extraction reads.</summary>
    /// <param name="lines">The message's lines, joined with CRLF as a mail transport writes them.</param>
    /// <returns>The content, carrying a fixed occurrence identity.</returns>
    public static RemoteEmailContent Message(params string[] lines) =>
        new(OccurrenceId, Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

    /// <summary>Turns MIME lines into stored content a read renders, recorded as intact.</summary>
    /// <param name="lines">The message's lines, joined with CRLF as a mail transport writes them.</param>
    /// <returns>The stored content, whose recorded length and digest describe the bytes beside them.</returns>
    /// <remarks>
    /// The recorded values are computed rather than stated, so a rendering test never fails on an integrity check it is
    /// not about. Damaged content is the read's own concern and is arranged where that behavior is asserted.
    /// </remarks>
    public static StoredEmailContent StoredMessage(params string[] lines) => StoredRawContent(
        Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

    /// <summary>Turns raw bytes into stored content, recorded as intact.</summary>
    /// <param name="rawMime">The raw payload.</param>
    /// <returns>The stored content, whose recorded length and digest describe the bytes beside them.</returns>
    public static StoredEmailContent StoredRawContent(byte[] rawMime)
    {
        ArgumentNullException.ThrowIfNull(rawMime);

        return new StoredEmailContent(rawMime, rawMime.LongLength, SHA256.HashData(rawMime));
    }

    /// <summary>Renders raw MIME as a read of a stored message does, asking for the document and the sender's own markup.</summary>
    /// <param name="rawMime">The raw payload.</param>
    /// <param name="authorIsProven">Whether the read established who wrote the message.</param>
    /// <returns>The rendering, having asserted that one was produced.</returns>
    public static async Task<EmailContentRendering> RenderingOf(ReadOnlyMemory<byte> rawMime, bool authorIsProven = false)
    {
        var renderer = new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions { MaxPartCount = 1000 });

        var result = await renderer.RenderAsync(
            StoredRawContent(rawMime.ToArray()),
            new EmailContentRenderingBounds(false, 100_000, int.MaxValue)
            {
                IncludeMailDocument = true,
                IncludeSelfContainedHtml = true,
                AuthorIsProven = authorIsProven,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(EmailContentRenderingOutcome.Rendered, result.Outcome);

        return result.Rendering!;
    }

    /// <summary>Renders raw MIME as <see cref="RenderingOf" /> does and answers its document.</summary>
    /// <param name="rawMime">The raw payload.</param>
    /// <param name="authorIsProven">Whether the read established who wrote the message.</param>
    /// <returns>The document, having asserted that one was produced.</returns>
    public static async Task<MailDocument> DocumentOf(ReadOnlyMemory<byte> rawMime, bool authorIsProven = false)
    {
        var document = (await RenderingOf(rawMime, authorIsProven)).Document;
        Assert.NotNull(document);

        return document;
    }
}
