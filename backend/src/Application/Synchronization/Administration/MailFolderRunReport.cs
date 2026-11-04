// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>What one folder's most recent turn through a synchronization run did.</summary>
/// <remarks>
/// <para>
/// The counts describe a run that reached its folder and are zero for every other outcome, because a folder that was
/// never opened stored nothing rather than storing none. The constructor is private and the two factory methods are the
/// only way to build one, so a caller cannot report a failure and a count together — the combination is unconstructible
/// rather than merely undocumented, which is what lets the administrative endpoint publish the counts without checking
/// the outcome beside them first.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. An outcome, four counts, and the moment the folder finished are the whole of
/// it, which is what makes it safe to serve from the administrative endpoint.
/// </para>
/// </remarks>
public sealed record MailFolderRunReport
{
    private MailFolderRunReport(
        MailFolderRunOutcome outcome,
        DateTimeOffset endedAt,
        int storedEmailCount,
        int skippedOversizedEmailCount,
        int unreadableMimeEmailCount,
        bool hasMoreEmails)
    {
        this.Outcome = outcome;
        this.EndedAt = endedAt;
        this.StoredEmailCount = storedEmailCount;
        this.SkippedOversizedEmailCount = skippedOversizedEmailCount;
        this.UnreadableMimeEmailCount = unreadableMimeEmailCount;
        this.HasMoreEmails = hasMoreEmails;
        this.OutcomeSince = endedAt;
        this.BehindSince = hasMoreEmails ? endedAt : null;
    }

    /// <summary>Gets how the folder's turn ended.</summary>
    public MailFolderRunOutcome Outcome { get; }

    /// <summary>Gets when it ended.</summary>
    public DateTimeOffset EndedAt { get; }

    /// <summary>Gets how many occurrences the run stored with their content.</summary>
    public int StoredEmailCount { get; }

    /// <summary>Gets how many it stored as metadata only, because their content exceeded a configured limit.</summary>
    public int SkippedOversizedEmailCount { get; }

    /// <summary>Gets how many stored occurrences carried MIME that enrichment could not read.</summary>
    public int UnreadableMimeEmailCount { get; }

    /// <summary>Gets whether the folder still held unprocessed mail when the run's batch budget ran out, which is what the next run resumes.</summary>
    public bool HasMoreEmails { get; }

    /// <summary>Gets when the folder's turns began ending the way this one did, which this process has watched without a break since.</summary>
    /// <remarks>
    /// A report carries it forward from the one it replaces while the outcome stays the same, and starts it at its own end
    /// otherwise, so it says since when a folder has been unreachable or failing rather than when it was last found so.
    /// It is what the client's freshness line measures its sixty seconds from.
    /// </remarks>
    public DateTimeOffset OutcomeSince { get; private init; }

    /// <summary>Gets since when the folder's turns have ended with mail still to take in, or <see langword="null" /> while this one did not.</summary>
    public DateTimeOffset? BehindSince { get; private init; }

    /// <summary>Reports a folder the run reached.</summary>
    /// <param name="endedAt">When the folder finished.</param>
    /// <param name="storedEmailCount">How many occurrences were stored with their content.</param>
    /// <param name="skippedOversizedEmailCount">How many were stored as metadata only.</param>
    /// <param name="unreadableMimeEmailCount">How many stored occurrences carried unreadable MIME.</param>
    /// <param name="hasMoreEmails">Whether unprocessed mail remains.</param>
    /// <returns>The report.</returns>
    public static MailFolderRunReport Synchronized(
        DateTimeOffset endedAt,
        int storedEmailCount,
        int skippedOversizedEmailCount,
        int unreadableMimeEmailCount,
        bool hasMoreEmails) =>
        new(
            MailFolderRunOutcome.Synchronized,
            endedAt,
            storedEmailCount,
            skippedOversizedEmailCount,
            unreadableMimeEmailCount,
            hasMoreEmails);

    /// <summary>Reports a folder the run did not synchronize, whatever kept it from doing so.</summary>
    /// <param name="outcome">Which of the unsynchronized outcomes ended the folder's turn.</param>
    /// <param name="endedAt">When it ended.</param>
    /// <returns>The report, carrying no counts.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> reports a folder that was synchronized, which is described by the other factory.</exception>
    public static MailFolderRunReport Unsynchronized(MailFolderRunOutcome outcome, DateTimeOffset endedAt)
    {
        if (outcome == MailFolderRunOutcome.Synchronized)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A folder the run reached is reported through Synchronized, which carries what it stored.");
        }

        return new MailFolderRunReport(
            outcome,
            endedAt,
            storedEmailCount: 0,
            skippedOversizedEmailCount: 0,
            unreadableMimeEmailCount: 0,
            hasMoreEmails: false);
    }

    /// <summary>Carries the instants a run of the same reading began at forward from the report this one replaces.</summary>
    /// <param name="previous">The folder's report before this one, or <see langword="null" /> for its first turn in this process.</param>
    /// <returns>This report, with its instants carried from <paramref name="previous" /> where the reading did not change.</returns>
    internal MailFolderRunReport Following(MailFolderRunReport? previous) => previous is null
        ? this
        : this with
        {
            OutcomeSince = previous.Outcome == this.Outcome ? previous.OutcomeSince : this.OutcomeSince,
            BehindSince = this.HasMoreEmails ? previous.BehindSince ?? this.BehindSince : null,
        };
}
