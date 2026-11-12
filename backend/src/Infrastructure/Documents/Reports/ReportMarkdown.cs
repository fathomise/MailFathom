// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MigraDoc.DocumentObjectModel;

namespace MailFathom.Infrastructure.Documents.Reports;

/// <summary>Sets a report's Markdown into the page, block by block, in the styles <see cref="ReportStyles" /> declares.</summary>
/// <remarks>
/// <para>
/// <strong>What is set:</strong> headings at three levels under the title, paragraphs, bulleted and numbered lists nested
/// up to three deep, quotations, tables whose header row repeats on every page they run onto, emphasis, strong emphasis,
/// code, and a thematic break. A heading is kept with what follows it, so it is never the last line of a page.
/// </para>
/// <para>
/// <strong>What is set as its words and nothing more:</strong> raw HTML, which the parser is told not to read, so a tag
/// is printed as the characters it is; a link, which is its words without its address; a picture, which is its
/// description and is never fetched; and an autolink, which is the address as text. Nothing the Markdown names becomes
/// something a viewer would open or fetch.
/// </para>
/// <para>
/// <strong>A citation</strong> is the marker <c>[^n]</c> for the <c>n</c>-th source, set as a superscript number. The
/// markers are found before the parse and carried through it as two characters from the Private Use Area around the
/// number, which no Markdown construct reads and which are first removed from whatever the model wrote, so a citation
/// survives emphasis, lists, and table cells, and nothing the model writes can forge one.
/// </para>
/// </remarks>
internal static partial class ReportMarkdown
{
    private const char CitationOpening = '\uE000';
    private const char CitationClosing = '\uE001';
    private const int DeepestListLevel = 3;
    private const string QuoteAttributionDash = "— ";
    private const double ListIndentPoints = 16;
    private const double ListMarkerPoints = 12;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().DisableHtml().Build();

    /// <summary>Sets a body of Markdown at the end of a section, cell, or other run of elements.</summary>
    /// <param name="elements">Where the blocks go.</param>
    /// <param name="markdown">The body, in which <c>[^n]</c> cites the <c>n</c>-th source.</param>
    /// <param name="sourceCount">How many sources the report lists, which bounds the citations set.</param>
    internal static void Set(DocumentElements elements, string markdown, int sourceCount)
    {
        var document = Markdown.Parse(WithCitationsCarried(markdown, sourceCount), Pipeline);

        SetBlocks(elements, document, listLevel: 0);
    }

    /// <summary>Replaces every citation marker naming a source with the form the parse carries, after removing any such form the text already held.</summary>
    internal static string WithCitationsCarried(string markdown, int sourceCount)
    {
        var cleared = markdown.Replace(CitationOpening.ToString(), string.Empty, StringComparison.Ordinal)
            .Replace(CitationClosing.ToString(), string.Empty, StringComparison.Ordinal);

        return CitationMarker().Replace(cleared, match =>
            int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= sourceCount
                ? string.Create(CultureInfo.InvariantCulture, $"{CitationOpening}{number}{CitationClosing}")
                : match.Value);
    }

    private static void SetBlocks(DocumentElements elements, ContainerBlock container, int listLevel)
    {
        foreach (var block in container)
        {
            SetBlock(elements, block, listLevel);
        }
    }

    private static void SetBlock(DocumentElements elements, Block block, int listLevel)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var headingParagraph = elements.AddParagraph();
                headingParagraph.Style = ReportStyles.HeadingOf(heading.Level);
                SetInlines(headingParagraph, heading.Inline);
                break;
            case ParagraphBlock paragraph:
                SetInlines(ParagraphIn(elements, listLevel), paragraph.Inline);
                break;
            case ListBlock list:
                SetList(elements, list, listLevel);
                break;
            case QuoteBlock quote:
                SetQuote(elements, quote);
                break;
            case Markdig.Extensions.Tables.Table table:
                ReportTables.Set(elements, table);
                break;
            case CodeBlock code:
                var codeParagraph = elements.AddParagraph();
                codeParagraph.Style = ReportStyles.Code;
                SetText(codeParagraph, code.Lines.ToString(), lineBreaks: true);
                break;
            case ThematicBreakBlock:
                elements.AddParagraph().Style = ReportStyles.Rule;
                break;
            case ContainerBlock nested:
                SetBlocks(elements, nested, listLevel);
                break;
            default:
                // A link reference definition and a blank line set nothing.
                break;
        }
    }

    /// <summary>Adds a paragraph for a block, indented to the list it stands in.</summary>
    private static Paragraph ParagraphIn(DocumentElements elements, int listLevel)
    {
        var paragraph = elements.AddParagraph();

        if (listLevel > 0)
        {
            paragraph.Style = ReportStyles.ListItem;
            paragraph.Format.LeftIndent = Unit.FromPoint(ListIndentPoints * listLevel);
        }

        return paragraph;
    }

    private static void SetList(DocumentElements elements, ListBlock list, int listLevel)
    {
        var level = Math.Min(listLevel + 1, DeepestListLevel);
        var first = true;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var start = elements.Count;

            SetBlocks(elements, item, level);

            if (start < elements.Count && elements[start] is Paragraph marked)
            {
                marked.Format.FirstLineIndent = Unit.FromPoint(-ListMarkerPoints);
                marked.Format.ListInfo = new ListInfo
                {
                    ListType = ListTypeOf(list.IsOrdered, level),
                    ContinuePreviousList = !first,
                    NumberPosition = Unit.FromPoint((ListIndentPoints * level) - ListMarkerPoints),
                };
                first = false;
            }
        }
    }

    private static ListType ListTypeOf(bool ordered, int level) => (ordered, level) switch
    {
        (true, 1) => ListType.NumberList1,
        (true, 2) => ListType.NumberList2,
        (true, _) => ListType.NumberList3,
        (false, 1) => ListType.BulletList1,
        (false, 2) => ListType.BulletList2,
        (false, _) => ListType.BulletList3,
    };

    /// <summary>
    /// Sets a quotation's blocks in the quotation style, so every paragraph of it carries the rule at its left; a last
    /// paragraph opening with a dash names who wrote it and when, and is set as that line rather than as their words.
    /// </summary>
    private static void SetQuote(DocumentElements elements, QuoteBlock quote)
    {
        var start = elements.Count;

        SetBlocks(elements, quote, listLevel: 0);

        var quoted = Enumerable.Range(start, elements.Count - start).Select(index => elements[index]).OfType<Paragraph>().ToArray();

        foreach (var paragraph in quoted)
        {
            paragraph.Style = ReportStyles.Quote;
        }

        if (quoted.Length > 1 && IsAttribution(quote.LastChild))
        {
            quoted[^1].Style = ReportStyles.QuoteSource;
        }
    }

    private static bool IsAttribution(Block? block) =>
        block is ParagraphBlock { Inline.FirstChild: LiteralInline literal } && literal.Content.ToString().StartsWith(QuoteAttributionDash, StringComparison.Ordinal);

    /// <summary>Sets one run of inline content into a paragraph.</summary>
    internal static void SetInlines(Paragraph paragraph, ContainerInline? inlines)
    {
        if (inlines is not null)
        {
            SetInlines(paragraph.AddFormattedText(), inlines);
        }
    }

    private static void SetInlines(FormattedText target, ContainerInline inlines)
    {
        foreach (var inline in inlines)
        {
            SetInline(target, inline);
        }
    }

    private static void SetInline(FormattedText target, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                SetText(target, literal.Content.ToString());
                break;
            case EmphasisInline emphasis:
                var emphasized = target.AddFormattedText();
                emphasized.Bold = emphasis.DelimiterCount >= 2;
                emphasized.Italic = emphasis.DelimiterCount is 1 or >= 3;
                SetInlines(emphasized, emphasis);
                break;
            case CodeInline code:
                var coded = target.AddFormattedText();
                coded.FontName = ReportFonts.Sans;
                SetText(coded, code.Content);
                break;
            case AutolinkInline autolink:
                SetText(target, autolink.Url);
                break;
            case LineBreakInline lineBreak:
                if (lineBreak.IsHard)
                {
                    target.AddLineBreak();
                }
                else
                {
                    target.AddText(" ");
                }

                break;
            case HtmlEntityInline entity:
                SetText(target, entity.Transcoded.ToString());
                break;
            case ContainerInline container:
                // A link or a picture is set as its words; its address is neither printed nor followed.
                SetInlines(target, container);
                break;
            default:
                break;
        }
    }

    /// <summary>Sets text, each citation it carries as a superscript number.</summary>
    private static void SetText(FormattedText target, string text)
    {
        var at = 0;

        foreach (Match citation in CarriedCitation().Matches(text))
        {
            AddPlain(target, text[at..citation.Index]);

            var mark = target.AddFormattedText(citation.Groups[1].Value);
            mark.Superscript = true;
            mark.FontName = ReportFonts.Sans;
            mark.Bold = true;
            mark.Color = ReportStyles.Mark;

            at = citation.Index + citation.Length;
        }

        AddPlain(target, text[at..]);
    }

    private static void SetText(Paragraph paragraph, string text, bool lineBreaks)
    {
        var target = paragraph.AddFormattedText();
        var lines = lineBreaks ? text.TrimEnd('\n', '\r').Split('\n') : [text];

        foreach (var (line, index) in lines.Select(static (line, index) => (line.TrimEnd('\r'), index)))
        {
            if (index > 0)
            {
                target.AddLineBreak();
            }

            SetText(target, line);
        }
    }

    /// <summary>Adds text with every control character, a tab included, set as a space, so nothing in it reads as layout.</summary>
    private static void AddPlain(FormattedText target, string text)
    {
        if (text.Length is 0)
        {
            return;
        }

        var plain = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            plain.Append(char.IsControl(character) ? ' ' : character);
        }

        target.AddText(plain.ToString());
    }

    [GeneratedRegex(@"\[\^(\d{1,3})\]", RegexOptions.CultureInvariant)]
    private static partial Regex CitationMarker();

    [GeneratedRegex(@"\uE000(\d{1,3})\uE001", RegexOptions.CultureInvariant)]
    private static partial Regex CarriedCitation();
}
