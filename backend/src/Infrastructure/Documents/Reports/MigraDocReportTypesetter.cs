// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Reports;
using MailFathom.Application.Localization;
using MigraDoc.Rendering;

namespace MailFathom.Infrastructure.Documents.Reports;

/// <summary>Sets a report as a PDF: Markdig reads its Markdown, MigraDoc lays the template out, and PDFsharp writes the file with the typefaces embedded.</summary>
/// <remarks>
/// <para>
/// <strong>Nothing is fetched and nothing is run.</strong> The layout engine is handed paragraphs, tables, and text that
/// were built here from the parse; no Markdown, markup, field code, or address reaches it as anything but characters.
/// </para>
/// <para>
/// <strong>The output depends on the report.</strong> The time the page states and the file's creation date are both the
/// report's, so the same report sets the same pages with the same words and dates. The engine still draws two values of
/// its own for every file — the six letters naming each embedded font subset, and the file identifier the PDF format
/// asks every file to carry — so two settings of one report are the same document without being the same octets.
/// </para>
/// <para>
/// Stateless and safe to share: every call composes and renders a document of its own, and the only shared state is the
/// typefaces PDFsharp keeps once read.
/// </para>
/// </remarks>
internal sealed class MigraDocReportTypesetter : IAgentReportTypesetter
{
    private const string Creator = "MailFathom";

    /// <inheritdoc />
    public AgentReportDocument Typeset(AgentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(report.Title, nameof(report));

        if (report.Title.Length > AgentReportBounds.MaximumTitleCharacters
            || report.Body.Length > AgentReportBounds.MaximumBodyCharacters
            || report.Sources.Count > AgentReportBounds.MaximumSources)
        {
            throw new ArgumentException("The report is past one of the bounds a report is held to.", nameof(report));
        }

        ReportFonts.Install();

        var renderer = new PdfDocumentRenderer
        {
            Document = ReportPage.Compose(report),
            Language = ApplicationTexts.CultureOf(report.Language).Name,
        };

        renderer.RenderDocument();

        var pdf = renderer.PdfDocument;
        pdf.Info.Creator = Creator;
        pdf.Info.CreationDate = report.WrittenAt.UtcDateTime;
        pdf.Info.ModificationDate = report.WrittenAt.UtcDateTime;

        // Read before saving: PDFsharp seals a document once it is written, and refuses even a read of its pages after.
        var pageCount = pdf.PageCount;
        using var stream = new MemoryStream();
        pdf.Save(stream, closeStream: false);

        return new AgentReportDocument(stream.ToArray(), pageCount);
    }
}
