// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Reports;

/// <summary>How much one report may hold.</summary>
/// <remarks>
/// Constants rather than settings, for the reason a conversation's own ceilings are: they describe what the product
/// does with a report. The body's ceiling is about twenty-five set pages, which is longer than anything a person reads
/// as a report and short enough that setting it costs a fraction of the run that wrote it; the PDF it sets is far under
/// what a conversation's file may hold.
/// </remarks>
public static class AgentReportBounds
{
    /// <summary>The longest title, in characters.</summary>
    public const int MaximumTitleCharacters = 200;

    /// <summary>The longest body, in characters of Markdown.</summary>
    public const int MaximumBodyCharacters = 60_000;

    /// <summary>The most sources one report lists, messages and files together.</summary>
    public const int MaximumSources = 60;
}
