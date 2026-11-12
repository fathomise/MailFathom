// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MigraDoc.DocumentObjectModel;

namespace MailFathom.Infrastructure.Documents.Reports;

/// <summary>The styles of a report's page: the design's report page, in points, declared once on the document.</summary>
/// <remarks>
/// The paper has one face whatever theme its reader uses — white, with the dark inks below — because a PDF is printed and
/// forwarded rather than drawn in a client. The sizes and colours are <c>design/styles/report-page.css</c>'s, which is
/// the page this template is held to.
/// </remarks>
internal static class ReportStyles
{
    /// <summary>The style of a quotation's paragraphs.</summary>
    internal const string Quote = "Quotation";

    /// <summary>The style of the line closing a quotation that names who wrote it and when, which opens with a dash.</summary>
    internal const string QuoteSource = "Quotation source";

    /// <summary>The style of a list item's paragraphs.</summary>
    internal const string ListItem = "List item";

    /// <summary>The style of a block of code.</summary>
    internal const string Code = "Code";

    /// <summary>The style of a thematic break, which is an empty paragraph with a rule under it.</summary>
    internal const string Rule = "Rule";

    /// <summary>The style of a table's cells.</summary>
    internal const string TableCell = "Table cell";

    /// <summary>The style of the line naming a source's sender and date, or a file's origin.</summary>
    internal const string SourceDetail = "Source detail";

    /// <summary>The ink the body is set in.</summary>
    internal static readonly Color Ink = Color.FromRgb(0x1d, 0x22, 0x30);

    /// <summary>The ink of a quotation and a minor heading.</summary>
    internal static readonly Color SecondInk = Color.FromRgb(0x3f, 0x45, 0x54);

    /// <summary>The ink of the frame: the date under the title, the running foot, a source's details.</summary>
    internal static readonly Color ThirdInk = Color.FromRgb(0x6a, 0x70, 0x80);

    /// <summary>The colour of a thin rule.</summary>
    internal static readonly Color RuleColor = Color.FromRgb(0xd9, 0xdc, 0xe3);

    /// <summary>The tint behind a table's header row and a block of code.</summary>
    internal static readonly Color Tint = Color.FromRgb(0xf3, 0xf4, 0xf7);

    /// <summary>The one accent on the paper: the kind above the title, a citation, a source's number.</summary>
    internal static readonly Color Mark = Color.FromRgb(0x2f, 0x4f, 0xb3);

    /// <summary>Names the style a Markdown heading of one level is set in.</summary>
    /// <param name="level">The heading's level, one for <c>#</c>.</param>
    /// <returns>
    /// The style. The title is the page's own and a report's sections open at <c>##</c>, as Markdown under a title does,
    /// so <c>#</c> and <c>##</c> are set alike as the first level under it, and <c>####</c> and deeper as the third.
    /// </returns>
    internal static string HeadingOf(int level) => level switch
    {
        <= 2 => StyleNames.Heading1,
        3 => StyleNames.Heading2,
        _ => StyleNames.Heading3,
    };

    /// <summary>Declares every style on a document.</summary>
    /// <param name="document">The document.</param>
    internal static void Declare(Document document)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = ReportFonts.Serif;
        normal.Font.Size = 10.5;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
        normal.ParagraphFormat.LineSpacing = 1.5;
        normal.ParagraphFormat.SpaceAfter = 9;

        Heading(document, StyleNames.Heading1, size: 15, lineSpacing: 1.25, spaceBefore: 8, Ink);
        Heading(document, StyleNames.Heading2, size: 12, lineSpacing: 1.3, spaceBefore: 4, Ink);
        Heading(document, StyleNames.Heading3, size: 10, lineSpacing: 1.35, spaceBefore: 0, SecondInk);

        var listItem = document.Styles.AddStyle(ListItem, StyleNames.Normal);
        listItem.ParagraphFormat.SpaceAfter = 3;

        var quote = document.Styles.AddStyle(Quote, StyleNames.Normal);
        quote.Font.Color = SecondInk;
        quote.ParagraphFormat.LeftIndent = 14;
        quote.ParagraphFormat.Borders.Left.Width = 2;
        quote.ParagraphFormat.Borders.Left.Color = RuleColor;
        quote.ParagraphFormat.Borders.DistanceFromLeft = 12;

        var quoteSource = document.Styles.AddStyle(QuoteSource, Quote);
        quoteSource.Font.Name = ReportFonts.Sans;
        quoteSource.Font.Size = 8.5;
        quoteSource.Font.Color = ThirdInk;

        var code = document.Styles.AddStyle(Code, StyleNames.Normal);
        code.Font.Name = ReportFonts.Sans;
        code.Font.Size = 8.5;
        code.ParagraphFormat.LineSpacing = 1.4;
        code.ParagraphFormat.Shading.Color = Tint;

        var rule = document.Styles.AddStyle(Rule, StyleNames.Normal);
        rule.Font.Size = 4;
        rule.ParagraphFormat.Borders.Bottom.Width = 0.75;
        rule.ParagraphFormat.Borders.Bottom.Color = RuleColor;

        var cell = document.Styles.AddStyle(TableCell, StyleNames.Normal);
        cell.Font.Name = ReportFonts.Sans;
        cell.Font.Size = 8.5;
        cell.Font.Color = SecondInk;
        cell.ParagraphFormat.LineSpacing = 1.4;
        cell.ParagraphFormat.SpaceAfter = 0;

        var detail = document.Styles.AddStyle(SourceDetail, StyleNames.Normal);
        detail.Font.Name = ReportFonts.Sans;
        detail.Font.Size = 8.5;
        detail.Font.Color = ThirdInk;

        var footer = document.Styles[StyleNames.Footer]!;
        footer.Font.Name = ReportFonts.Sans;
        footer.Font.Size = 8;
        footer.Font.Color = ThirdInk;
        footer.ParagraphFormat.SpaceAfter = 0;
        footer.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;
        footer.ParagraphFormat.Borders.Top.Width = 0.75;
        footer.ParagraphFormat.Borders.Top.Color = RuleColor;
        footer.ParagraphFormat.Borders.DistanceFromTop = 7;
    }

    private static void Heading(
        Document document,
        string name,
        double size,
        double lineSpacing,
        double spaceBefore,
        Color color)
    {
        var heading = document.Styles[name]!;
        heading.Font.Name = ReportFonts.Sans;
        heading.Font.Size = size;
        heading.Font.Bold = true;
        heading.Font.Color = color;
        heading.ParagraphFormat.LineSpacing = lineSpacing;
        heading.ParagraphFormat.SpaceBefore = spaceBefore;
        heading.ParagraphFormat.KeepWithNext = true;
    }
}
