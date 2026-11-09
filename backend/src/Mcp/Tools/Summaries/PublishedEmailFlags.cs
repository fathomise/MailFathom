// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.Tools.Summaries;

/// <summary>Publishes the flags MailFathom holds for one email.</summary>
/// <remarks>
/// Every description on this type reaches the client inside the tool's output schema, so each one states what the value
/// means on the wire rather than how it is stored.
/// </remarks>
[Description("The flags MailFathom holds for the email. On an account mirrored from a mail server they follow what that server last reported, and a change made through MailFathom shows here as soon as it is made; on an account MailFathom alone keeps they are MailFathom's own state. Reading mail through MailFathom never changes them.")]
internal sealed record PublishedEmailFlags
{
    /// <summary>Gets whether the email is marked read.</summary>
    [Description("Whether the email is marked read.")]
    public required bool Seen { get; init; }

    /// <summary>Gets whether the email is marked answered.</summary>
    [Description("Whether the email is marked answered.")]
    public required bool Answered { get; init; }

    /// <summary>Gets whether the email is flagged for attention.</summary>
    [Description("Whether the email is flagged for attention.")]
    public required bool Flagged { get; init; }

    /// <summary>Gets whether the email is marked a draft.</summary>
    [Description("Whether the email is marked a draft.")]
    public required bool Draft { get; init; }

    /// <summary>Gets whether the email is marked deleted but not yet expunged.</summary>
    [Description("Whether the email is marked deleted but not yet expunged from the folder.")]
    public required bool Deleted { get; init; }

    /// <summary>Gets the keywords the email carries, which are the flags nobody standardized.</summary>
    [Description("The keywords the email carries, such as $JUNK or a label a mail client set, in upper case and without duplicates. Flag names are compared without regard to case, so the case a keyword is written in never decides a match; an empty list means the email carries none, or that nothing has observed this email yet.")]
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>Gets when a server last reported the flags, or <see langword="null" /> when no run has observed them.</summary>
    [Description("When a mail server last reported these flags, as an ISO 8601 timestamp, or null when no synchronization run has observed this email yet.")]
    public DateTimeOffset? ObservedAt { get; init; }

    /// <summary>Gets whether the flags have been recorded at all.</summary>
    [Description("Whether these flags have been recorded rather than never observed. When false, every flag above is false because nothing has recorded them yet, not because the email carries none of them.")]
    public required bool WasObserved { get; init; }

    /// <summary>Publishes one stored flag set.</summary>
    /// <param name="flags">The flags the summary carried.</param>
    /// <returns>The wire representation of <paramref name="flags" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flags" /> is <see langword="null" />.</exception>
    public static PublishedEmailFlags From(StoredEmailFlags flags)
    {
        ArgumentNullException.ThrowIfNull(flags);

        return new PublishedEmailFlags
        {
            Seen = flags.IsSeen,
            Answered = flags.IsAnswered,
            Flagged = flags.IsFlagged,
            Draft = flags.IsDraft,
            Deleted = flags.IsDeleted,
            Keywords = flags.Keywords.Values,
            ObservedAt = flags.ObservedAt,
            WasObserved = flags.WasObserved,
        };
    }
}
