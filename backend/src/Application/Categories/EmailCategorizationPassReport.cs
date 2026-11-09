// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;

namespace MailFathom.Application.Categories;

/// <summary>What one categorization pass did, in counts alone.</summary>
/// <param name="CategorizedEmailCount">How many messages a model answered about, including one whose answer a competing replica had already recorded.</param>
/// <param name="UnansweredEmailCount">How many it asked about and got nothing readable for.</param>
/// <param name="StoppedBy">What ended the pass before its mail did, or <see langword="null" /> where nothing did.</param>
/// <param name="EmailsRemain">Whether mail it did not reach is still owed a category.</param>
public sealed record EmailCategorizationPassReport(
    int CategorizedEmailCount,
    int UnansweredEmailCount,
    DecisionModelOutcome? StoppedBy,
    bool EmailsRemain)
{
    /// <summary>Gets the report of a pass that had nothing to do.</summary>
    public static EmailCategorizationPassReport Nothing { get; } = new(0, 0, StoppedBy: null, EmailsRemain: false);

    /// <summary>Gets whether the pass has nothing to report.</summary>
    public bool IsEmpty => this.CategorizedEmailCount == 0 && this.UnansweredEmailCount == 0 && this.StoppedBy is null;
}
