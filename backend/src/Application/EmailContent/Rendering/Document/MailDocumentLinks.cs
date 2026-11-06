// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document.Blocks;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>Reads every link a document holds, in reading order, with the words it was linked from.</summary>
/// <remarks>
/// One walk for every reader of a message's links, so two readers cannot come to disagree about which blocks hold one.
/// A picture's link has no words, which is what the absent text says.
/// </remarks>
public static class MailDocumentLinks
{
    /// <summary>Reads the links of one document.</summary>
    /// <param name="document">The document to read.</param>
    /// <returns>Each link with its words, in reading order, a link reached from several runs once per run.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public static IEnumerable<(MailDocumentLink Link, string? Words)> Of(MailDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Blocks.SelectMany(LinkedRunsOf);
    }

    private static IEnumerable<(MailDocumentLink Link, string? Words)> LinkedRunsOf(MailDocumentBlock block) => block switch
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

    private static IEnumerable<(MailDocumentLink Link, string? Words)> RunsWithLinks(IReadOnlyList<MailInlineRun> runs) =>
        runs.Where(static run => run.Link is not null).Select(static run => (run.Link!, (string?)run.Text));
}
