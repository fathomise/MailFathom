// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>Holds the flags MailFathom stores for one email, read from its row.</summary>
/// <param name="ObservedAt">
/// When a server last reported these flags, or <see langword="null" /> while none has. On a row stored with no occurrence
/// on any server it is the moment the row was stored, with nothing observed.
/// </param>
/// <param name="IsSeen">Whether the <c>\Seen</c> mark is set.</param>
/// <param name="IsAnswered">Whether <c>\Answered</c> is set.</param>
/// <param name="IsFlagged">Whether <c>\Flagged</c> is set.</param>
/// <param name="IsDraft">Whether <c>\Draft</c> is set.</param>
/// <param name="IsDeleted">Whether <c>\Deleted</c> is set.</param>
/// <param name="Keywords">The keywords held beside those five, which are flags nobody standardized.</param>
/// <remarks>
/// <para>
/// This is what the row holds, as distinct from <see cref="RemoteEmailFlagSnapshot" />, which is what one server said.
/// On a mirrored account a value here is the server's last report, or what a person's act wrote while its change is
/// outstanding; on a held account it is MailFathom's own state.
/// </para>
/// <para>
/// <paramref name="ObservedAt" /> is what separates "none of these flags are set" from "nobody has looked yet", which no
/// combination of the booleans can express on its own: <see cref="NeverObserved" /> carries every flag as
/// <see langword="false" /> and no keyword at all.
/// </para>
/// </remarks>
public sealed record StoredEmailFlags(
    DateTimeOffset? ObservedAt,
    bool IsSeen,
    bool IsAnswered,
    bool IsFlagged,
    bool IsDraft,
    bool IsDeleted,
    EmailKeywords Keywords)
{
    /// <summary>Gets the flags of an email no run has observed yet.</summary>
    public static StoredEmailFlags NeverObserved { get; } = new(
        ObservedAt: null,
        IsSeen: false,
        IsAnswered: false,
        IsFlagged: false,
        IsDraft: false,
        IsDeleted: false,
        EmailKeywords.None);

    /// <summary>Gets whether these flags have been recorded rather than never observed.</summary>
    public bool WasObserved => this.ObservedAt is not null;
}
