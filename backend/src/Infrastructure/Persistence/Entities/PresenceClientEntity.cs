// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One client a member keeps open, renewed by that client every minute and counted only until its expiry.</summary>
/// <remarks>
/// <para>
/// Keyed by the member and the identifier the client minted for itself, so a renewal is an upsert of one row and a
/// guessed identifier names nothing of anybody else's: the member comes from the session, never from the request.
/// </para>
/// <para>
/// The row holds nothing but whether a person is using the client and when it stops counting — no address, user agent,
/// device, or replica — and no row belongs to a replica, which is why a replica dying needs no handling. It is
/// overwritten by each renewal and removed when the client closes or after it expires, so it never becomes a history.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PresenceClientEntity
{
    /// <summary>The table these rows live in, named here because every statement against it is composed.</summary>
    internal const string TableName = "presence_clients";

    /// <summary>The column naming whose client it is.</summary>
    internal const string UserIdColumnName = "UserId";

    /// <summary>The column holding the identifier the client minted for itself.</summary>
    internal const string ClientIdColumnName = "ClientId";

    /// <summary>The column holding whether a person is using the client.</summary>
    internal const string ActivityColumnName = "Activity";

    /// <summary>The column holding when the row stops counting.</summary>
    internal const string ExpiresAtColumnName = "ExpiresAt";

    /// <summary>Gets or sets the member the client belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the identifier the client minted for itself when it started.</summary>
    public Guid ClientId { get; set; }

    /// <summary>Gets or sets whether a person was using the client at its last renewal, as the domain's numeric value.</summary>
    public int Activity { get; set; }

    /// <summary>Gets or sets when the row stops counting unless renewed again, in UTC.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
