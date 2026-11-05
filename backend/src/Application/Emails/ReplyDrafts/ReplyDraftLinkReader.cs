// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>Reads the addresses a draft may link to: each web address the person typed, and each link the correspondence carries.</summary>
/// <remarks>
/// <para>
/// The correspondence's links are read from the document a person reading the message is shown, rendered by the same
/// renderer, so a target here is one the reading pane already draws and nothing a provider wrote. It reads the local
/// store alone, so it opens no IMAP session and cannot touch a remote <c>\Seen</c> flag.
/// </para>
/// <para>
/// A link the reduction warns about is left out — a displayed host differing from the target's, or a host whose
/// Unicode form may imitate another — because the host is all a person is shown beside a drafted link before they
/// accept it, and a host alone cannot expose either. The reading pane shows such a link with its warning, and a reply
/// has no reason to carry it. A link with no host to show, such as a <c>mailto</c> address, is left out for the same
/// reason.
/// </para>
/// </remarks>
public sealed class ReplyDraftLinkReader
{
    /// <summary>The most links one drafting publishes.</summary>
    /// <remarks>A reply links to a document or two the exchange named; a list longer than this is a newsletter's footer rather than something to cite.</remarks>
    public const int MaximumLinks = 40;

    /// <summary>The longest run of words one link is published with.</summary>
    public const int MaximumWordsLength = 200;

    /// <summary>How many characters of each representation of a body are read for its links.</summary>
    /// <remarks>The figure a reading pane renders a body to by default, so the links are those a person reading the message is shown.</remarks>
    private const int CharactersReadForLinks = 100_000;

    private static readonly char[] TrailingPunctuation = ['.', ',', ';', ':', '!', '?', ')', ']', '}', '>', '"', '\''];

    private readonly IEmailContentStore contentStore;
    private readonly IEmailContentRenderer renderer;

    /// <summary>Initializes the reader.</summary>
    /// <param name="contentStore">Finds each message's stored content.</param>
    /// <param name="renderer">Renders it into the document a reader is shown.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ReplyDraftLinkReader(IEmailContentStore contentStore, IEmailContentRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(renderer);

        this.contentStore = contentStore;
        this.renderer = renderer;
    }

    /// <summary>Reads the links one drafting may cite, numbered in the order the turn publishes them.</summary>
    /// <param name="instruction">What the person typed, whose web addresses come first.</param>
    /// <param name="messages">The messages the drafting is written from, in the turn's order.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The links, each target once, at most <see cref="MaximumLinks" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    /// <remarks>The messages are read one after another because they share the store's unit of work.</remarks>
    public async Task<IReadOnlyList<ReplyDraftLink>> ReadAsync(
        string? instruction,
        IReadOnlyList<ReplyDraftMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var found = new List<(MailDocumentLink Link, string? Words)>(TypedIn(instruction));

        foreach (var message in messages)
        {
            found.AddRange(await this.LinkedInAsync(message.StoredEmailId, cancellationToken));
        }

        return Numbered(found);
    }

    /// <summary>Reads each web address the person typed, as the words around it are separated.</summary>
    private static IEnumerable<(MailDocumentLink Link, string? Words)> TypedIn(string? instruction) =>
        (instruction ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(static word => word.TrimEnd(TrailingPunctuation))
            .Where(static word => word.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || word.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Select(static word => MailLinkReader.Read(word, word))
            .OfType<MailDocumentLink>()
            .Select(static link => (link, (string?)null));

    /// <summary>Numbers what may be cited, leaving out what a host cannot honestly describe.</summary>
    private static IReadOnlyList<ReplyDraftLink> Numbered(IEnumerable<(MailDocumentLink Link, string? Words)> found) =>
    [
        .. found
            .Where(static linked => linked.Link.Host is not null && !linked.Link.IsWorthWarningAbout && IsWeb(linked.Link))
            .DistinctBy(static linked => linked.Link.Target, StringComparer.Ordinal)
            .Take(MaximumLinks)
            .Select(static (linked, position) => new ReplyDraftLink(position, linked.Link, WordsOf(linked.Words))),
    ];

    private static bool IsWeb(MailDocumentLink link) =>
        Uri.TryCreate(link.Target, UriKind.Absolute, out var target)
        && (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps);

    private static string? WordsOf(string? words) =>
        string.IsNullOrWhiteSpace(words)
            ? null
            : MailTextBounds.TruncateAtTextElementBoundary(words.Trim(), MaximumWordsLength);

    private async Task<IEnumerable<(MailDocumentLink Link, string? Words)>> LinkedInAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        if (await this.contentStore.FindStoredContentAsync(storedEmailId, cancellationToken) is not { } content)
        {
            return [];
        }

        var rendered = await this.renderer.RenderAsync(
            content,
            new EmailContentRenderingBounds(
                IncludeSanitizedHtml: false,
                CharactersReadForLinks,
                RemainingCharactersForRead: int.MaxValue)
            {
                IncludeMailDocument = true,
            },
            cancellationToken);

        return rendered.Rendering?.Document is { } document ? MailDocumentLinks.Of(document) : [];
    }
}
