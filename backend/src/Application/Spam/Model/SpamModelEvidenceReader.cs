// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Spam.Model;

/// <summary>Reads, out of one stored message, exactly what the model stage sends and nothing else.</summary>
/// <remarks>
/// <para>
/// The message is rendered by the same renderer a person's read goes through, so the body the model reads is the text a
/// person would be shown, and each link is judged by <see cref="MailLinkReader" /> — the one place a link's words are
/// held against where it goes. Only the host survives the reading: a target's path and query often carry a token
/// naming the recipient, and they are dropped here, before anything could send them.
/// </para>
/// <para>
/// It reads the local store alone, so it opens no IMAP session and cannot touch a remote <c>\Seen</c> flag.
/// </para>
/// </remarks>
public sealed class SpamModelEvidenceReader
{
    /// <summary>How many characters of each representation of the body are read for its links before the body sent is cut.</summary>
    /// <remarks>
    /// The figure a reading pane renders a body to by default, so the model is shown the links of the span a person
    /// reading the message would be shown. It is separate from the body's own bound because the links are mined from the
    /// markup, which is mostly styling: a body sent at 8 000 characters would otherwise leave the markup none, and the
    /// anchors behind a long stylesheet would never be parsed.
    /// </remarks>
    internal const int CharactersReadForLinks = 100_000;

    private readonly IEmailContentRenderer renderer;
    private readonly IEmailProvenanceReader provenanceReader;
    private readonly SpamModelStageBounds bounds;

    /// <summary>Initializes the reader.</summary>
    /// <param name="renderer">Renders the stored message into its plain text, its document, and its attachments.</param>
    /// <param name="provenanceReader">Answers what the account's trusted receiving server established about the message's author, and whether this deployment trusts them.</param>
    /// <param name="bounds">How much of a body one call carries.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SpamModelEvidenceReader(
        IEmailContentRenderer renderer,
        IEmailProvenanceReader provenanceReader,
        SpamModelStageBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(provenanceReader);
        ArgumentNullException.ThrowIfNull(bounds);

        this.renderer = renderer;
        this.provenanceReader = provenanceReader;
        this.bounds = bounds;
    }

    /// <summary>Reads one message's evidence.</summary>
    /// <param name="emailId">The stored message.</param>
    /// <param name="content">Its stored content.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The evidence, or <see langword="null" /> where the stored content cannot be rendered at all.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    public async Task<SpamModelEvidence?> ReadAsync(
        StoredEmailId emailId,
        StoredEmailContent content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var renderingBounds = new EmailContentRenderingBounds(
            IncludeSanitizedHtml: false,
            CharactersReadForLinks,
            RemainingCharactersForRead: int.MaxValue)
        {
            IncludeMailDocument = true,
        };

        var rendered = await this.renderer.RenderAsync(content, renderingBounds, cancellationToken);

        if (rendered.Rendering is not { } rendering)
        {
            return null;
        }

        var provenance = await this.provenanceReader.ReadAsync([emailId], cancellationToken);
        var verification = provenance.TryGetValue(emailId, out var recorded)
            ? recorded.Sender
            : SenderVerification.NotEstablished;
        var sender = AddressOf(rendering.Headers, EmailAddressRole.From) ?? AddressOf(rendering.Headers, EmailAddressRole.Sender);
        var replyTo = AddressOf(rendering.Headers, EmailAddressRole.ReplyTo);
        var body = MailTextBounds.TruncateAtTextElementBoundary(
            rendering.PlainTextBody.Text,
            this.bounds.MaximumBodyCharacters);

        return new SpamModelEvidence
        {
            SenderAddress = sender?.Address,
            SenderDisplayName = sender?.DisplayName is { Length: > 0 } displayName ? displayName : null,
            DifferingReplyToAddress = replyTo is { } reply && reply.NormalizedAddress != sender?.NormalizedAddress
                ? reply.Address
                : null,
            Subject = rendering.Headers.Subject,
            Body = body,
            IsBodyCut = rendering.PlainTextBody.WasTruncated || body.Length < rendering.PlainTextBody.Text.Length,
            Links = LinksOf(rendering.Document),
            Attachments = [.. rendering.Attachments.Take(SpamModelEvidence.MaximumAttachments).Select(AttachmentOf)],
            AuthorAuthentication = verification.AuthorAuthentication,
            IsSenderTrusted = verification.DeploymentTrust is SenderTrustLevel.Trusted,
        };
    }

    private static EmailAddress? AddressOf(EmailContentHeaders headers, EmailAddressRole role) =>
        headers.Participants.FirstOrDefault(participant => participant.Role == role)?.Address;

    private static SpamModelAttachment AttachmentOf(ExtractedEmailAttachment attachment) => new(
        attachment.MediaType,
        attachment.FileName is { } name
            ? MailTextBounds.TruncateAtTextElementBoundary(name.Value, SpamModelEvidence.MaximumFileNameLength)
            : null);

    /// <summary>Reads the host of every followable web link the document holds, in reading order, with what its words claimed.</summary>
    /// <remarks>
    /// A link to an address or another scheme with no host is not a place a reader is sent, so it says nothing the model
    /// could weigh and is left out. One host linked from several runs is one fact, so each pairing is kept once.
    /// </remarks>
    private static IReadOnlyList<SpamModelLink> LinksOf(MailDocument? document) =>
    [
        .. (document?.Blocks ?? [])
            .SelectMany(LinkedRunsOf)
            .Where(static linked => (linked.Link.AsciiHost ?? linked.Link.Host) is not null)
            .Select(static linked => new SpamModelLink(
                (linked.Link.AsciiHost ?? linked.Link.Host)!,
                linked.Text is { } text ? MailLinkReader.HostNamedBy(text) : null,
                linked.Link.Deception is MailLinkDeception.DisplayedHostDiffers))
            .Distinct()
            .Take(SpamModelEvidence.MaximumLinks),
    ];

    private static IEnumerable<(MailDocumentLink Link, string? Text)> LinkedRunsOf(MailDocumentBlock block) => block switch
    {
        MailParagraphBlock paragraph => RunsWithLinks(paragraph.Content),
        MailHeadingBlock heading => RunsWithLinks(heading.Content),
        MailImageBlock { Link: { } link } => [(link, null)],
        MailQuoteBlock quote => quote.Blocks.SelectMany(LinkedRunsOf),
        MailListBlock list => list.Items.SelectMany(static item => item.Blocks).SelectMany(LinkedRunsOf),
        MailTableBlock table => table.Rows
            .SelectMany(static row => row.Cells)
            .SelectMany(static cell => cell.Blocks)
            .SelectMany(LinkedRunsOf),
        _ => [],
    };

    private static IEnumerable<(MailDocumentLink Link, string? Text)> RunsWithLinks(IReadOnlyList<MailInlineRun> runs) =>
        runs.Where(static run => run.Link is not null).Select(static run => (run.Link!, (string?)run.Text));
}
