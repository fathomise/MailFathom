// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Reports;

/// <summary>A report the Agent wrote, as the service sets it: a title, a body of Markdown, and the sources it closes with.</summary>
/// <param name="Title">The title, which heads the first page, runs in every page's foot, and names the file.</param>
/// <param name="Body">The report itself, in Markdown, in which a citation is written as <c>[^n]</c> for the <c>n</c>-th of <paramref name="Sources" />.</param>
/// <param name="WrittenAt">When the report was written, on the person's own clock, which the first page states.</param>
/// <param name="Language">The person's language, which every word the service adds to the report is written in.</param>
/// <param name="Sources">What the report rests on, numbered from one in this order: the messages it cites, then the files attached to the question it cites.</param>
/// <remarks>
/// <para>
/// <strong>The model writes Markdown and nothing else.</strong> The page is the service's: the typeface, the title block,
/// the running foot, and the list of sources come from one template, so a report looks the same whoever asked for it and
/// nothing a message carried can decide what the file contains beyond the words of its body.
/// </para>
/// <para>
/// <strong>Every source was minted from what the run read</strong>, never from a name the model wrote: a citation the
/// model wrote naming something the run was not shown was dropped before the report reached this record, so every
/// <c>[^n]</c> left in the body names an entry of <paramref name="Sources" />.
/// </para>
/// <para>
/// <strong>Everything here is personal data</strong>: the body retells mail, and the sources name who wrote it. None of
/// it reaches a log, a span, a metric, or a failure message.
/// </para>
/// </remarks>
public sealed record AgentReport(
    string Title,
    string Body,
    DateTimeOffset WrittenAt,
    UserLanguage Language,
    IReadOnlyList<AgentReportSource> Sources)
{
    /// <summary>Writes the marker the body cites one source with.</summary>
    /// <param name="number">The source's number, counted from one in the order of <see cref="Sources" />.</param>
    /// <returns>The marker.</returns>
    public static string CitationOf(int number) => string.Create(CultureInfo.InvariantCulture, $"[^{number}]");
}
