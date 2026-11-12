// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Census;

/// <summary>One bounded reading of how much mail a scope and its filters hold, and how it falls into groups.</summary>
/// <param name="Groups">The leading groups, at most as many as the reading asked for, in the order <see cref="MailCensusGroup" /> states.</param>
/// <param name="GroupCount">How many groups the mail falls into in all, the leading ones included.</param>
/// <param name="CountedInGroups">How many messages the groups count between them, which exceeds <paramref name="Total" /> where a message counts in several groups and falls short of it where a message counts in none.</param>
/// <param name="Total">How many messages the scope and the filters hold.</param>
/// <remarks>
/// Every figure is exact for what was counted, because the database counted it in one statement rather than this
/// process estimating it from a page. A reading grouped by nothing carries no groups and only <paramref name="Total" />.
/// </remarks>
public sealed record MailCensus(
    IReadOnlyList<MailCensusGroup> Groups,
    int GroupCount,
    int CountedInGroups,
    int Total)
{
    /// <summary>Gets a census of a scope holding no mail at all.</summary>
    public static MailCensus Empty { get; } = new([], 0, 0, 0);

    /// <summary>Gets how many groups lie beyond the leading ones this reading carries.</summary>
    public int GroupsBeyond => this.GroupCount - this.Groups.Count;

    /// <summary>Gets how many messages the groups beyond the leading ones count between them.</summary>
    public int CountedBeyond => this.CountedInGroups - this.Groups.Sum(static group => group.Count);
}
