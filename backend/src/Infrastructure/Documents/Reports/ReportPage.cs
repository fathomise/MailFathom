// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Text.RegularExpressions;
using MailFathom.Application.Agent.Reports;
using MailFathom.Application.Localization;
using MigraDoc.DocumentObjectModel;

namespace MailFathom.Infrastructure.Documents.Reports;

/// <summary>The template every report is set from: an A4 page with the title block on the first, the running foot on each, the body, and the sources that close it.</summary>
/// <remarks>
/// Every word the page adds — the kind above the title, when it was written, the page count, the sources' heading — is the
/// person's language, read from the application's own texts; the body is the model's, and a quotation in it stays in the
/// language it was written in.
/// </remarks>
internal static partial class ReportPage
{
    /// <summary>The width of an A4 page, in points.</summary>
    internal const double WidthPoints = 595;

    /// <summary>The margin at either side, in points.</summary>
    internal const double SideMarginPoints = 57;

    private const double HeightPoints = 842;
    private const string DatePattern = "d MMMM yyyy, HH:mm";
    private const int LongestFootTitle = 80;
    private const double SourceNumberPoints = 22;

    /// <summary>Composes the whole of one report as a document of the layout engine.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The document, ready to be rendered.</returns>
    internal static Document Compose(AgentReport report)
    {
        var culture = ApplicationTexts.CultureOf(report.Language);
        var document = new Document();
        document.Info.Title = report.Title;

        ReportStyles.Declare(document);

        var section = document.AddSection();
        section.PageSetup.PageWidth = WidthPoints;
        section.PageSetup.PageHeight = HeightPoints;
        section.PageSetup.TopMargin = 56;
        section.PageSetup.BottomMargin = 72;
        section.PageSetup.LeftMargin = SideMarginPoints;
        section.PageSetup.RightMargin = SideMarginPoints;
        section.PageSetup.FooterDistance = 30;

        SetFoot(section, report);
        SetTitleBlock(section.Elements, report, culture);
        ReportMarkdown.Set(section.Elements, report.Body, report.Sources.Count);
        SetSources(section.Elements, report, culture);

        return document;
    }

    private static void SetTitleBlock(DocumentElements elements, AgentReport report, CultureInfo culture)
    {
        var kind = elements.AddParagraph(Text(ApplicationText.AgentReportKind, report).ToUpper(culture));
        kind.Format.Font.Name = ReportFonts.Sans;
        kind.Format.Font.Size = 8;
        kind.Format.Font.Bold = true;
        kind.Format.Font.Color = ReportStyles.Mark;
        kind.Format.SpaceAfter = 6;

        var title = elements.AddParagraph(report.Title);
        title.Format.Font.Name = ReportFonts.Sans;
        title.Format.Font.Size = 22;
        title.Format.Font.Bold = true;
        title.Format.LineSpacing = 1.15;
        title.Format.SpaceAfter = 6;

        var when = string.Format(culture, Text(ApplicationText.AgentReportWritten, report), report.WrittenAt.ToString(DatePattern, culture));
        var basis = BasisOf(report, culture);
        var written = elements.AddParagraph(basis is null ? when : $"{when} · {basis}");
        written.Format.Font.Name = ReportFonts.Sans;
        written.Format.Font.Size = 9;
        written.Format.Font.Color = ReportStyles.ThirdInk;
        written.Format.Borders.Bottom.Width = 1.5;
        written.Format.Borders.Bottom.Color = ReportStyles.Ink;
        written.Format.Borders.DistanceFromBottom = 14;
        written.Format.SpaceAfter = 20;
    }

    /// <summary>Says how many messages and attached files the report rests on, or nothing where it cites none.</summary>
    private static string? BasisOf(AgentReport report, CultureInfo culture)
    {
        var messages = report.Sources.Count(static source => source.Kind is AgentReportSourceKind.Message);
        var files = report.Sources.Count - messages;
        var counted = (messages, files) switch
        {
            (0, 0) => null,
            (_, 0) => Count(messages, ApplicationText.AgentReportBasisOneMessage, ApplicationText.AgentReportBasisMessages, report, culture),
            (0, _) => Count(files, ApplicationText.AgentReportBasisOneFile, ApplicationText.AgentReportBasisFiles, report, culture),
            _ => string.Format(
                culture,
                Text(ApplicationText.AgentReportBasisMessagesAndFiles, report),
                Count(messages, ApplicationText.AgentReportBasisOneMessage, ApplicationText.AgentReportBasisMessages, report, culture),
                Count(files, ApplicationText.AgentReportBasisOneFile, ApplicationText.AgentReportBasisFiles, report, culture)),
        };

        return counted is null ? null : string.Format(culture, Text(ApplicationText.AgentReportBasis, report), counted);
    }

    // After "based on", both languages need only one form for one and another for every other count: Polish takes the
    // genitive there, whose plural is the same for two, five, and twenty-one.
    private static string Count(
        int count,
        ApplicationText one,
        ApplicationText many,
        AgentReport report,
        CultureInfo culture) =>
        string.Format(culture, Text(count is 1 ? one : many, report), count);

    /// <summary>Sets the running foot: the title at the left and which page of how many at the right.</summary>
    private static void SetFoot(Section section, AgentReport report)
    {
        var foot = section.Footers.Primary.AddParagraph();
        foot.Style = StyleNames.Footer;
        foot.Format.TabStops.AddTabStop(Unit.FromPoint(WidthPoints - (2 * SideMarginPoints)), TabAlignment.Right);
        foot.AddText(report.Title.Length <= LongestFootTitle ? report.Title : report.Title[..(LongestFootTitle - 1)].TrimEnd() + "…");
        foot.AddTab();

        var template = Text(ApplicationText.AgentReportPage, report);
        var at = 0;

        foreach (Match placeholder in Placeholder().Matches(template))
        {
            foot.AddText(template[at..placeholder.Index]);

            if (placeholder.Groups[1].Value is "0")
            {
                foot.AddPageField();
            }
            else
            {
                foot.AddNumPagesField();
            }

            at = placeholder.Index + placeholder.Length;
        }

        foot.AddText(template[at..]);
    }

    /// <summary>Sets the list of sources that closes the report, numbered as the body cites them.</summary>
    private static void SetSources(DocumentElements elements, AgentReport report, CultureInfo culture)
    {
        elements.AddParagraph(Text(ApplicationText.AgentReportSources, report), StyleNames.Heading1);

        if (report.Sources.Count is 0)
        {
            elements.AddParagraph(Text(ApplicationText.AgentReportNoSources, report), ReportStyles.SourceDetail);

            return;
        }

        var numbered = report.Sources.Select(static (source, index) => (Source: source, Number: index + 1)).ToArray();
        var messages = numbered.Where(static entry => entry.Source.Kind is AgentReportSourceKind.Message).ToArray();
        var files = numbered.Where(static entry => entry.Source.Kind is AgentReportSourceKind.AttachedFile).ToArray();

        if (messages.Length > 0)
        {
            var introduction = files.Length > 0 ? ApplicationText.AgentReportSourcesIntroductionWithFiles : ApplicationText.AgentReportSourcesIntroduction;
            elements.AddParagraph(Text(introduction, report), ReportStyles.SourceDetail);

            foreach (var (source, number) in messages)
            {
                SetSource(elements, number, source.Title, Detail(source.Author, source.At, culture));
            }
        }

        if (files.Length > 0)
        {
            elements.AddParagraph(Text(ApplicationText.AgentReportAttachedFiles, report), StyleNames.Heading3);

            foreach (var (source, number) in files)
            {
                SetSource(elements, number, source.Title, Detail(Text(ApplicationText.AgentReportAttachedByYou, report), source.At, culture));
            }
        }
    }

    private static void SetSource(
        DocumentElements elements,
        int number,
        string title,
        string detail)
    {
        var entry = elements.AddParagraph();
        entry.Format.LeftIndent = SourceNumberPoints;
        entry.Format.FirstLineIndent = -SourceNumberPoints;
        entry.Format.TabStops.AddTabStop(SourceNumberPoints);
        entry.Format.SpaceBefore = 6;
        entry.Format.SpaceAfter = 0;
        entry.Format.KeepTogether = true;
        entry.Format.Borders.Bottom.Width = 0.5;
        entry.Format.Borders.Bottom.Color = ReportStyles.RuleColor;
        entry.Format.Borders.DistanceFromBottom = 6;

        var mark = entry.AddFormattedText(number.ToString(CultureInfo.InvariantCulture));
        mark.FontName = ReportFonts.Sans;
        mark.Size = 8;
        mark.Bold = true;
        mark.Color = ReportStyles.Mark;
        entry.AddTab();
        entry.AddFormattedText(title).Bold = true;

        if (detail.Length > 0)
        {
            entry.AddLineBreak();

            var detailText = entry.AddFormattedText(detail);
            detailText.FontName = ReportFonts.Sans;
            detailText.Size = 8.5;
            detailText.Color = ReportStyles.ThirdInk;
        }
    }

    private static string Detail(string? author, DateTimeOffset? at, CultureInfo culture) =>
        string.Join(" · ", new[] { author, at?.ToString(DatePattern, culture) }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    private static string Text(ApplicationText text, AgentReport report) => ApplicationTexts.GetText(text, report.Language);

    [GeneratedRegex(@"\{([01])\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
