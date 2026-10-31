// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What writing one column of a user's row did — the label, the title, or the type: nothing at all, nothing this deployment holds, or the value.</summary>
/// <param name="UserHeld">Whether this deployment holds a user under that identifier.</param>
/// <param name="RefusalMessage">The sentence naming what has to change first, or <see langword="null" /> where the row carries the value.</param>
/// <remarks>
/// The two facts are apart because a boundary answers them apart. A user this deployment does not hold is the same
/// answer here as at every other user-scoped route — the deployment holds no such record — while an unusable value is a
/// refusal about the value rather than about the user, judged before anybody is looked up.
/// </remarks>
internal readonly record struct UserRowWriteOutcome(bool UserHeld, string? RefusalMessage)
{
    /// <summary>Gets the outcome of a row that now carries the value.</summary>
    internal static UserRowWriteOutcome Written { get; } = new(UserHeld: true, RefusalMessage: null);

    /// <summary>Gets the outcome of an identifier this deployment holds no user under.</summary>
    internal static UserRowWriteOutcome NoSuchUser { get; } = new(UserHeld: false, RefusalMessage: null);

    /// <summary>Gets whether the row carries the value.</summary>
    internal bool IsWritten => this.UserHeld && this.RefusalMessage is null;

    /// <summary>Reports that the user is held and the value was not written.</summary>
    /// <param name="refusalMessage">The sentence naming the correction.</param>
    /// <returns>The refused outcome.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusalMessage" /> is <see langword="null" />, empty, or white space.</exception>
    internal static UserRowWriteOutcome Refused(string refusalMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusalMessage);

        return new UserRowWriteOutcome(UserHeld: true, refusalMessage);
    }
}
