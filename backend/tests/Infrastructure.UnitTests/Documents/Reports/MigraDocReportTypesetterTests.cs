// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Reports;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Documents.Reports;
using UglyToad.PdfPig;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Documents.Reports;

/// <summary>Covers how a report the Agent wrote is set as a PDF: what of its Markdown is interpreted, what is printed as written, and what the template adds.</summary>
public sealed class MigraDocReportTypesetterTests
{
    private static readonly DateTimeOffset WrittenAt = new(2026, 11, 12, 10, 30, 0, TimeSpan.FromHours(1));

    private readonly MigraDocReportTypesetter typesetter = new();

    /// <summary>Every block Markdown has is set as its words, and none of the marks that shaped it reaches the page.</summary>
    [Fact]
    public void Typeset_EveryBlockMarkdownHas_SetsEachAsItsWordsWithoutItsMarks()
    {
        // Arrange
        const string Body = """
            ## Findings

            The move is *planned* and **paid** with `code`.

            - Kestrel Quay
              - the second floor
            1. Sign the lease

            > Przeprowadzka jest potwierdzona.

            ---
            """;

        // Act
        var text = TextOf(this.typesetter.Typeset(Report(Body)));

        // Assert
        Assert.All(
            ["Findings", "Themoveisplannedandpaidwithcode.", "KestrelQuay", "thesecondfloor", "Signthelease", "Przeprowadzkajestpotwierdzona."],
            words => Assert.Contains(words, text, StringComparison.Ordinal));
        Assert.All(["##", "**", "`", "> "], mark => Assert.DoesNotContain(mark, text, StringComparison.Ordinal));
    }

    /// <summary>Raw HTML is printed as the characters it was written in, and a link or a picture as its words, its address neither printed nor followed.</summary>
    [Fact]
    public void Typeset_RawHtmlALinkAndAPicture_PrintsTheMarkupAsWrittenAndNoAddress()
    {
        // Arrange
        const string Body = """
            <script>alert(1)</script>

            Read [the offer](https://example.com/offer) beside ![a chart](https://example.com/chart.png).
            """;

        // Act
        var text = TextOf(this.typesetter.Typeset(Report(Body)));

        // Assert
        Assert.Contains("<script>alert(1)</script>", text, StringComparison.Ordinal);
        Assert.Contains("Readtheofferbesideachart.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", text, StringComparison.Ordinal);
    }

    /// <summary>A table longer than a page runs onto the next one, which repeats its header row above the rows it continues with.</summary>
    [Fact]
    public void Typeset_ATableLongerThanAPage_RepeatsItsHeaderOnEveryPageItRunsOnto()
    {
        // Arrange
        var rows = string.Join('\n', Enumerable.Range(1, 90).Select(static row => $"| Vendor {row} | {row * 10} |"));
        var body = $"| Supplier | Price |\n|---|---:|\n{rows}";

        // Act
        var document = this.typesetter.Typeset(Report(body));

        // Assert
        var pages = PagesOf(document);

        Assert.True(document.PageCount >= 2);
        Assert.Equal(document.PageCount, pages.Count);
        Assert.Contains("Vendor90", pages[^1], StringComparison.Ordinal);
        Assert.All(pages.Where(static page => page.Contains("Vendor", StringComparison.Ordinal)), page => Assert.Contains("SupplierPrice", page, StringComparison.Ordinal));
    }

    /// <summary>
    /// The report closes with its sources, messages first and the question's files after, each cited in the body by its
    /// number; a citation past what the report lists is printed as written rather than set as a number nothing answers.
    /// </summary>
    [Fact]
    public void Typeset_ASourcedReport_ClosesWithItsSourcesCitedByNumber()
    {
        // Arrange
        var report = Report("Moved[^1] and paid[^2] as offered[^3], or so[^9].") with
        {
            Sources =
            [
                new(AgentReportSourceKind.Message, "Kestrel Quay move", "Anna Nowak", WrittenAt.AddDays(-3)),
                new(AgentReportSourceKind.Message, "Invoice 114", "billing@example.com", WrittenAt.AddDays(-2)),
                new(AgentReportSourceKind.AttachedFile, "Offer v3.pdf", Author: null, WrittenAt.AddDays(-1)),
            ],
        };

        // Act
        var text = TextOf(this.typesetter.Typeset(report));

        // Assert
        Assert.Contains("Moved1andpaid2asoffered3,orso[^9].", text, StringComparison.Ordinal);
        Assert.Contains("Sources", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("KestrelQuaymove", StringComparison.Ordinal) < text.IndexOf("Invoice114", StringComparison.Ordinal)
            && text.IndexOf("Invoice114", StringComparison.Ordinal) < text.IndexOf("Filesattachedtothequestion", StringComparison.Ordinal)
            && text.IndexOf("Filesattachedtothequestion", StringComparison.Ordinal) < text.IndexOf("Offerv3.pdf", StringComparison.Ordinal));
        Assert.Contains("Written12November2026,10:30·basedon2messagesand1attachedfile", text, StringComparison.Ordinal);
        Assert.Contains("Themessagesthereportrestson,intheorderitfirstcitesthem,andthefilesattachedtothequestion.", text, StringComparison.Ordinal);
        Assert.Contains("AnnaNowak·9November2026,10:30", text, StringComparison.Ordinal);
        Assert.Contains("attachedbyyou", text, StringComparison.Ordinal);
    }

    /// <summary>A quotation's last line opening with a dash names who wrote it, set apart from their words in the frame's face rather than the body's.</summary>
    [Fact]
    public void Typeset_AQuotationClosedByADashedLine_SetsThatLineAsItsAttribution()
    {
        // Arrange
        const string Body = """
            > We accept the two-hour response.
            >
            > — Anna Nowak, 14 August 2026
            """;

        // Act
        var document = this.typesetter.Typeset(Report(Body));

        // Assert
        using var pdf = PdfDocument.Open(document.Content);

        // A capital A is printed nowhere on the page but in the attribution, and a capital W in the body only at "We".
        var letters = pdf.GetPage(1).Letters;

        Assert.Contains(letters, static letter => letter.Value is "A" && letter.FontName?.Contains(ReportFonts.Sans, StringComparison.Ordinal) is true);
        Assert.Contains(letters, static letter => letter.Value is "W" && letter.FontName?.Contains(ReportFonts.Serif, StringComparison.Ordinal) is true);
    }

    /// <summary>A report citing nothing says so where its sources would be, rather than closing on an empty heading.</summary>
    [Fact]
    public void Typeset_AReportCitingNothing_SaysItCitesNothing()
    {
        // Act
        var text = TextOf(this.typesetter.Typeset(Report("Nothing was found.")));

        // Assert
        Assert.Contains("Thisreportcitesnomessageandnofile.", text, StringComparison.Ordinal);
    }

    /// <summary>A report in Polish sets the page's own words in Polish and its diacritics in the embedded serif, which the file carries rather than borrows.</summary>
    [Fact]
    public void Typeset_AReportInPolish_SetsItsWordsAndDiacriticsInTheEmbeddedTypeface()
    {
        // Arrange
        var report = Report("Zażółć gęślą jaźń.") with { Title = "Przeprowadzka", Language = UserLanguage.Polish };

        // Act
        var document = this.typesetter.Typeset(report);

        // Assert
        using var pdf = PdfDocument.Open(document.Content);
        var page = pdf.GetPage(1);
        var text = Squeezed(page.Text);

        Assert.All(
            ["RAPORT", "Napisany12listopada2026,10:30", "Zażółćgęśląjaźń.", "Źródła", "Strona1z1"],
            words => Assert.Contains(words, text, StringComparison.Ordinal));
        Assert.Contains(page.Letters, static letter => letter.FontName?.Contains(ReportFonts.Serif, StringComparison.Ordinal) is true);
        Assert.Equal("Przeprowadzka", pdf.Information.Title);
    }

    /// <summary>The same report set twice sets the same pages, and the file is dated when the report was written rather than when it was set.</summary>
    [Fact]
    public void Typeset_TheSameReportTwice_SetsTheSamePagesDatedWhenItWasWritten()
    {
        // Arrange
        var report = Report("## Findings\n\nThe move is on 3 October.");

        // Act
        var first = this.typesetter.Typeset(report);
        var second = this.typesetter.Typeset(report);

        // Assert
        using var pdf = PdfDocument.Open(second.Content);

        Assert.Equal(PagesOf(first), PagesOf(second));
        Assert.Equal(WrittenAt, pdf.Information.GetCreatedDateTimeOffset());
    }

    /// <summary>A report past what one holds is refused rather than set, because the tool's own bounds are what keep a file from growing without one.</summary>
    [Fact]
    public void Typeset_ATitlePastItsBound_IsRefused()
    {
        // Arrange
        var report = Report("Body.") with { Title = new string('t', AgentReportBounds.MaximumTitleCharacters + 1) };

        // Act
        var refused = Record.Exception(() => this.typesetter.Typeset(report));

        // Assert
        Assert.IsType<ArgumentException>(refused);
    }

    private static AgentReport Report(string body) => new("The move", body, WrittenAt, UserLanguage.English, []);

    private static string TextOf(AgentReportDocument document) => string.Concat(PagesOf(document));

    private static List<string> PagesOf(AgentReportDocument document)
    {
        using var pdf = PdfDocument.Open(document.Content);

        return [.. pdf.GetPages().Select(static page => Squeezed(page.Text))];
    }

    /// <summary>Drops every space, because which of them the extraction recovers depends on glyph spacing rather than on the words.</summary>
    private static string Squeezed(string text) => new([.. text.Where(static character => !char.IsWhiteSpace(character))]);
}
