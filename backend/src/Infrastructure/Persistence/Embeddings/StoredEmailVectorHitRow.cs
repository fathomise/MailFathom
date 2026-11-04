// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>What the vector ranking query returns for one email, before anything is read about the email itself.</summary>
/// <remarks>
/// <para>
/// The distance is expressed in whatever units the profile's metric uses and is comparable only within one query. It is
/// carried out of the adapter for the ordering it produced and for nothing else: no caller publishes it, because a
/// number whose scale changes with the embedding model would tell a client something it could not act on.
/// </para>
/// <para>
/// The members are set by name rather than through a constructor because the ranking orders the rows after projecting
/// them: the provider resolves a member a row was initialized with back to the column it came from, and cannot see
/// through a constructor argument to do the same.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailVectorHitRow
{
    /// <summary>Gets the stable local identity of the ranked email.</summary>
    public required Guid StoredEmailId { get; init; }

    /// <summary>Gets when the last receiving hop recorded the message, which with the identity is its place in the timeline order.</summary>
    public required DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>Gets how far the email's nearest embedded passage sits from the query, smaller being nearer.</summary>
    public required double Distance { get; init; }
}
