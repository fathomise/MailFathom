// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.Mail.Mime.Rendering;
using MimeKit;

namespace MailFathom.Benchmarks;

/// <summary>What a reading-pane read of a message carrying its own pictures costs, and what of it is a second decode.</summary>
/// <remarks>
/// <para>
/// A read that asks for the self-contained markup beside the document tree resolves the message's pictures once per
/// representation, so every picture the markup can still afford is decoded and encoded a second time. The markup's
/// octet allowance is what the document left of the read's, so the repeated work is largest when the pictures come to
/// half of that allowance and shrinks towards nothing as they approach the whole of it; the two picture counts below
/// are those two ends.
/// </para>
/// <para>
/// <see cref="ResolveMarkupPictures" /> is the repeated decode on its own, under exactly the allowance the markup is
/// handed, which is the part a shared decode could remove. Held against <see cref="RenderDocumentAndMarkup" /> it says
/// what share of the read that is.
/// </para>
/// <para>
/// The message is composed here rather than drawn from <c>LargeSyntheticMessage</c>, because nothing in the synthetic
/// corpus carries a picture its body names, and that is the whole of what this path does differently. What a picture
/// holds decides nothing a decode does, so its octets are a fixed pattern and every run measures the same message.
/// </para>
/// </remarks>
public class InlinePictureRenderingBenchmarks
{
    /// <summary>The size of one picture, under the per-picture bound so every one of them is drawable.</summary>
    private const int PictureOctets = 500_000;

    private readonly MimeKitEmailContentRenderer renderer = new(new EmailMimeExtractionOptions());

    private StoredEmailContent content = null!;
    private MimeMessage parsed = null!;
    private HashSet<string> named = null!;
    private int markupImageOctets;

    /// <summary>Gets or sets how many pictures the message's body draws.</summary>
    /// <remarks>
    /// Four come to half the read's allowance, where the markup can afford every picture the document drew; eight come
    /// to nearly all of it, where the markup can afford none and each decode it starts is abandoned at its bound.
    /// </remarks>
    [Params(4, 8)]
    public int PictureCount { get; set; }

    /// <summary>Composes the message and reads the markup's allowance off a document render, outside what is measured.</summary>
    /// <returns>A task that completes once the message is composed.</returns>
    [GlobalSetup]
    public async Task ComposeMessageAsync()
    {
        var builder = new BodyBuilder();

        var pictures = Enumerable.Range(0, this.PictureCount)
            .Select(index =>
            {
                var picture = builder.LinkedResources.Add(
                    $"picture-{index}.png",
                    [.. Enumerable.Range(0, PictureOctets).Select(octet => (byte)(octet + index))],
                    ContentType.Parse("image/png"));

                picture.ContentId = $"picture-{index}@example.test";

                return picture.ContentId;
            })
            .ToArray();

        builder.HtmlBody = string.Concat(pictures.Select(id => $"<p>Picture</p><img src=\"cid:{id}\" alt=\"\">"));

        using var message = new MimeMessage
        {
            Subject = "Pictures",
            Body = builder.ToMessageBody(),
        };

        message.From.Add(new MailboxAddress("Sender", "sender@example.test"));
        message.To.Add(new MailboxAddress("Mailbox", "mailbox@example.test"));

        using var raw = new MemoryStream();
        message.WriteTo(raw);

        var rawMime = raw.ToArray();

        this.content = new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime));
        using var parsing = new MemoryStream(rawMime);

        this.parsed = await MimeMessage.LoadAsync(parsing);
        this.named = [.. pictures.Select(MailInlineImages.KeyOf)];

        var rendered = await this.renderer.RenderAsync(
            this.content,
            Bounds(includeMarkup: false),
            CancellationToken.None);

        var document = rendered.Rendering?.Document
            ?? throw new InvalidOperationException("The measured message rendered no document.");

        this.markupImageOctets = (int)Math.Max(
            MailDocumentBounds.Default.MaximumInlineImageOctetsPerDocument - MailDocumentImages.OctetsIn(document),
            0);
    }

    /// <summary>Releases the parsed message the isolated decode reads.</summary>
    [GlobalCleanup]
    public void ReleaseMessage() => this.parsed.Dispose();

    /// <summary>Renders the document tree alone, which is what the pane asks for when the full markup is closed.</summary>
    /// <returns>What the render produced, returned so nothing about it can be optimized away.</returns>
    [Benchmark(Baseline = true)]
    public Task<EmailContentRenderingResult> RenderDocument() =>
        this.renderer.RenderAsync(this.content, Bounds(includeMarkup: false), CancellationToken.None);

    /// <summary>Renders the document tree and the self-contained markup, which is a read with the full markup open.</summary>
    /// <returns>What the render produced, returned so nothing about it can be optimized away.</returns>
    [Benchmark]
    public Task<EmailContentRenderingResult> RenderDocumentAndMarkup() =>
        this.renderer.RenderAsync(this.content, Bounds(includeMarkup: true), CancellationToken.None);

    /// <summary>Resolves the pictures under the markup's allowance alone, which is the decode a shared one would save.</summary>
    /// <returns>How many pictures were resolved, returned so nothing about the resolution can be optimized away.</returns>
    [Benchmark]
    public async Task<int> ResolveMarkupPictures()
    {
        var bounds = MailDocumentBounds.Default;

        var images = await MailInlineImages.ResolveAsync(
            this.parsed,
            this.named,
            bounds.MaximumInlineImages,
            bounds.MaximumInlineImageOctets,
            this.markupImageOctets,
            CancellationToken.None);

        return images.ResolvedCount;
    }

    /// <summary>Composes the bounds a client body read hands the renderer, with or without the full markup.</summary>
    private static EmailContentRenderingBounds Bounds(bool includeMarkup) =>
        new(IncludeSanitizedHtml: false, MaxCharactersPerRepresentation: 100_000, RemainingCharactersForRead: 200_000)
        {
            IncludeMailDocument = true,
            IncludeSelfContainedHtml = includeMarkup,
            RemainingInlineImageOctetsForRead = MailDocumentBounds.Default.MaximumInlineImageOctetsPerDocument,
        };
}
