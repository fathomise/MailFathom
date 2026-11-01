// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Sessions;

/// <summary>What a session knows of the client it was signed in from, which a renewal carries forward unchanged.</summary>
/// <param name="Kind">The head the client declared at sign-in.</param>
/// <param name="LastOpenedOn">The UTC day a phone session was last opened, and <see langword="null" /> for every other kind.</param>
/// <remarks>
/// A day rather than an instant, because all it answers is whether the phone app was opened within the last seven days,
/// and an instant would be a record of when somebody picked up their phone. It is overwritten by the next opening and
/// goes with the session, so it never becomes a history.
/// </remarks>
public sealed record SessionClient(ClientKind Kind, DateOnly? LastOpenedOn)
{
    /// <summary>Describes the client a sign-in at an instant declared, a phone counting as opened on that day.</summary>
    /// <param name="kind">The head the client declared.</param>
    /// <param name="instant">The time of the sign-in.</param>
    /// <returns>The client the new session records.</returns>
    public static SessionClient SignedInAt(ClientKind kind, DateTimeOffset instant) =>
        new(kind, kind == ClientKind.Android ? DateOnly.FromDateTime(instant.UtcDateTime) : null);
}
