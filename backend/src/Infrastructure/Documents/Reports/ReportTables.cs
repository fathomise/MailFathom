// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Markdig.Syntax;
using MigraDoc.DocumentObjectModel;

namespace MailFathom.Infrastructure.Documents.Reports;

/// <summary>Sets a Markdown table as a table of the page, whose header row repeats on every page it runs onto.</summary>
/// <remarks>
/// Markdown and the page both name a table <c>Table</c>, a row, and a cell, so every type of either is written in full
/// here rather than one of them read as the other.
/// </remarks>
internal static class ReportTables
{
    /// <summary>The width the page leaves a table, between the side margins, in points.</summary>
    internal const double WidthPoints = ReportPage.WidthPoints - (2 * ReportPage.SideMarginPoints);

    private const double PaddingPoints = 5;

    /// <summary>Sets one table.</summary>
    /// <param name="elements">Where it goes.</param>
    /// <param name="markdown">The table as the Markdown parsed it.</param>
    internal static void Set(DocumentElements elements, Markdig.Extensions.Tables.Table markdown)
    {
        var rows = markdown.OfType<Markdig.Extensions.Tables.TableRow>().ToArray();
        var columns = Math.Max(markdown.ColumnDefinitions.Count, rows.Select(static row => row.Count).DefaultIfEmpty(0).Max());

        if (columns is 0)
        {
            return;
        }

        var table = elements.AddTable();
        table.Style = ReportStyles.TableCell;
        table.TopPadding = PaddingPoints;
        table.BottomPadding = PaddingPoints;
        table.LeftPadding = 6;
        table.RightPadding = 6;

        foreach (var _ in Enumerable.Range(0, columns))
        {
            table.AddColumn(Unit.FromPoint(WidthPoints / columns));
        }

        foreach (var row in rows)
        {
            SetRow(table, row, markdown.ColumnDefinitions, columns);
        }

        // The paragraph after a table carries the space a table has no margin of its own for.
        elements.AddParagraph().Format.SpaceAfter = 0;
    }

    private static void SetRow(
        MigraDoc.DocumentObjectModel.Tables.Table table,
        Markdig.Extensions.Tables.TableRow markdown,
        List<Markdig.Extensions.Tables.TableColumnDefinition> definitions,
        int columns)
    {
        var row = table.AddRow();
        row.HeadingFormat = markdown.IsHeader;
        row.Borders.Bottom.Width = markdown.IsHeader ? 1 : 0.5;
        row.Borders.Bottom.Color = markdown.IsHeader ? ReportStyles.Ink : ReportStyles.RuleColor;
        row.VerticalAlignment = markdown.IsHeader ? MigraDoc.DocumentObjectModel.Tables.VerticalAlignment.Bottom : MigraDoc.DocumentObjectModel.Tables.VerticalAlignment.Top;

        if (markdown.IsHeader)
        {
            row.Shading.Color = ReportStyles.Tint;
            row.Format.Font.Bold = true;
            row.Format.Font.Color = ReportStyles.Ink;
        }

        foreach (var (cell, index) in markdown.OfType<Markdig.Extensions.Tables.TableCell>().Take(columns).Select(static (cell, index) => (cell, index)))
        {
            var paragraph = row.Cells[index].AddParagraph();
            paragraph.Format.Alignment = AlignmentOf(index < definitions.Count ? definitions[index].Alignment : null);

            foreach (var leaf in cell.OfType<LeafBlock>())
            {
                ReportMarkdown.SetInlines(paragraph, leaf.Inline);
            }
        }
    }

    private static ParagraphAlignment AlignmentOf(Markdig.Extensions.Tables.TableColumnAlign? alignment) => alignment switch
    {
        Markdig.Extensions.Tables.TableColumnAlign.Right => ParagraphAlignment.Right,
        Markdig.Extensions.Tables.TableColumnAlign.Center => ParagraphAlignment.Center,
        _ => ParagraphAlignment.Left,
    };
}
