// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Reports;

/// <summary>Sets a report the Agent wrote as a PDF, from the one template every report is set from.</summary>
/// <remarks>
/// <para>
/// The Markdown is read, never run: headings, paragraphs, lists, tables, quotations, emphasis, and code are set, raw
/// HTML is set as the text it is, and a picture or a link is set as its words — no address the Markdown names is
/// fetched, opened, or written into the file as something a reader's viewer would follow.
/// </para>
/// <para>
/// The output depends on the report alone: the time the first page states is the report's, never a clock's, so the
/// same report sets the same pages with the same words.
/// </para>
/// </remarks>
public interface IAgentReportTypesetter
{
    /// <summary>Sets one report.</summary>
    /// <param name="report">The report, within <see cref="AgentReportBounds" />.</param>
    /// <returns>The PDF, and how many pages it was set on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="report" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the report is past one of <see cref="AgentReportBounds" />.</exception>
    AgentReportDocument Typeset(AgentReport report);
}
