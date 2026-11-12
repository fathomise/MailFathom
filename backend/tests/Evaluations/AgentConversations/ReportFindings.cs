// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>Reads a report's Markdown for what a case asks of it: the findings it lists, whether each cites, which structures it holds, and its own words apart from what it quotes.</summary>
/// <remarks>
/// It reads lines rather than parsing, because what a case asks is a structure a line settles: a list item or a table
/// row is one line, a citation is a numbered marker the tool wrote, and a quotation is every line opening with
/// <c>&gt;</c>.
/// </remarks>
internal static partial class ReportFindings
{
    /// <summary>Lists the findings a report sets: every list item and every table row below a header.</summary>
    /// <param name="body">The report's Markdown, its citations numbered.</param>
    /// <returns>Each finding's line, trimmed.</returns>
    public static IReadOnlyList<string> Of(string body)
    {
        var lines = LinesOf(body);
        var items = lines.Where(static line => ListItem().IsMatch(line));
        var rows = lines
            .Select(static (line, index) => (Line: line, Index: index))
            .Where(entry => TableRow().IsMatch(entry.Line)
                && !TableSeparator().IsMatch(entry.Line)
                && entry.Index > 0
                && !(entry.Index + 1 < lines.Count && TableSeparator().IsMatch(lines[entry.Index + 1])))
            .Select(static entry => entry.Line);

        return [.. items.Concat(rows).Select(static line => line.Trim())];
    }

    /// <summary>Tells whether a finding cites a source.</summary>
    /// <param name="finding">One finding's line.</param>
    /// <returns>Whether it carries a numbered citation.</returns>
    public static bool Cites(string finding) => Citation().IsMatch(finding);

    /// <summary>Tells whether a report holds a structure.</summary>
    /// <param name="body">The report's Markdown.</param>
    /// <param name="structure">The structure.</param>
    /// <returns>Whether a line of it sets that structure.</returns>
    public static bool Holds(string body, ReportStructure structure) => structure switch
    {
        ReportStructure.Table => LinesOf(body).Any(static line => TableSeparator().IsMatch(line)),
        ReportStructure.NestedList => LinesOf(body).Any(static line => NestedListItem().IsMatch(line)),
        ReportStructure.Quotation => LinesOf(body).Any(static line => QuotationLine().IsMatch(line)),
        _ => throw new ArgumentOutOfRangeException(nameof(structure), structure, "No such structure."),
    };

    /// <summary>Reads a report's own words: every line but the quotations, which stay in the language the mail was written in.</summary>
    /// <param name="body">The report's Markdown.</param>
    /// <returns>Its lines outside quotations.</returns>
    public static string OwnWordsOf(string body) =>
        string.Join('\n', LinesOf(body).Where(static line => !QuotationLine().IsMatch(line)));

    private static List<string> LinesOf(string body) => [.. body.ReplaceLineEndings("\n").Split('\n')];

    [GeneratedRegex(@"^\s*([-*+]|\d{1,3}[.)])\s+\S", RegexOptions.CultureInvariant)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\s{2,}([-*+]|\d{1,3}[.)])\s+\S", RegexOptions.CultureInvariant)]
    private static partial Regex NestedListItem();

    [GeneratedRegex(@"^\s*\|.*\|\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableRow();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"^\s{0,3}>", RegexOptions.CultureInvariant)]
    private static partial Regex QuotationLine();

    [GeneratedRegex(@"\[\^\d{1,3}\]", RegexOptions.CultureInvariant)]
    private static partial Regex Citation();
}
