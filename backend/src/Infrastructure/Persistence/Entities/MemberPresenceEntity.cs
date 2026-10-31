// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The one row per member that holds the presence they chose and when they were last seen.</summary>
/// <remarks>
/// <para>
/// Beside the user record rather than in it, because it is written on every renewal of every open client while the
/// record is written by people, and because no settings policy governs it: an administrator never sets somebody's
/// presence.
/// </para>
/// <para>
/// It holds the present alone and is overwritten in place, so nothing keeps an earlier state, an earlier time, or when
/// a choice was made. It goes with the user.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MemberPresenceEntity
{
    /// <summary>The table these rows live in, named here because every statement against it is composed.</summary>
    internal const string TableName = "member_presence";

    /// <summary>The key column, naming the member.</summary>
    internal const string UserIdColumnName = "UserId";

    /// <summary>The column holding the chosen state.</summary>
    internal const string ChosenStateColumnName = "ChosenState";

    /// <summary>The column holding the while the choice was made for.</summary>
    internal const string ChoiceDurationColumnName = "ChoiceDuration";

    /// <summary>The column holding when the choice runs out.</summary>
    internal const string ChoiceEndsAtColumnName = "ChoiceEndsAt";

    /// <summary>The column holding when the member was last seen.</summary>
    internal const string LastSeenAtColumnName = "LastSeenAt";

    /// <summary>Gets or sets the member.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the chosen state as the domain's numeric value, or <see langword="null" /> where the member chose none.</summary>
    public int? ChosenState { get; set; }

    /// <summary>Gets or sets the while the choice was made for as the domain's numeric value, or <see langword="null" /> where it lasts until changed or there is none.</summary>
    public int? ChoiceDuration { get; set; }

    /// <summary>Gets or sets when the choice runs out, or <see langword="null" /> where it lasts until changed or there is none.</summary>
    public DateTimeOffset? ChoiceEndsAt { get; set; }

    /// <summary>Gets or sets when the member was last present, or <see langword="null" /> where they never were.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }
}
