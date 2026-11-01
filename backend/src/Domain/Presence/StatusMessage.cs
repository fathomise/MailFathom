// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;

namespace MailFathom.Domain.Presence;

/// <summary>What a member tells the other members of their organization they are doing, and when that stops being said.</summary>
/// <param name="Text">The message, trimmed, of at most <see cref="MostCharacters" /> characters, holding nothing <see cref="ContactText" /> refuses in a contact's name.</param>
/// <param name="Clears">The while the member picked, or <see langword="null" /> where it never clears. Kept beside <paramref name="ClearsAt" /> because the status menu shows which one was picked, which the instant alone cannot say.</param>
/// <param name="ClearsAt">The instant the message stops being shown, or <see langword="null" /> where it never clears.</param>
/// <remarks>A message that cleared is not removed by anybody: whoever reads it compares <paramref name="ClearsAt" /> with the time of the read, as a chosen presence is read.</remarks>
public sealed record StatusMessage(string Text, PresenceDuration? Clears, DateTimeOffset? ClearsAt)
{
    /// <summary>The most characters a message holds, counted as UTF-16 code units, which is what a browser's text field counts.</summary>
    public const int MostCharacters = 280;

    /// <summary>Makes the message a member wrote, resolving when it clears in their own time zone.</summary>
    /// <param name="text">What they wrote.</param>
    /// <param name="clears">When it clears, or <see langword="null" /> for never.</param>
    /// <param name="writtenAt">When they wrote it.</param>
    /// <param name="zone">The member's own time zone, which is where <em>today</em> and <em>this week</em> end.</param>
    /// <returns>The message, or <see langword="null" /> where nothing but white space was written, which clears it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> or <paramref name="zone" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the text is longer than <see cref="MostCharacters" />, is not well-formed UTF-16, or holds a scalar that renders as something other than text; the message is the rule, worded for whoever wrote it.</exception>
    /// <remarks>The message is drawn beside a member's name for everybody in their organization, so it is held to the rule a contact's name is: a line break, a control character, or a bidirectional override would make it say something the member did not see, and is refused rather than stripped.</remarks>
    public static StatusMessage? Written(
        string text,
        PresenceDuration? clears,
        DateTimeOffset writtenAt,
        UserTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(zone);

        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > MostCharacters)
        {
            throw new ArgumentException($"A status message holds at most {MostCharacters} characters.");
        }

        if (!ContactText.IsWellFormed(trimmed) || trimmed.EnumerateRunes().Any(ContactText.IsUnprintable))
        {
            throw new ArgumentException("A status message is one line of printable text, with no line break, control, or formatting character.");
        }

        return clears is { } lasting
            ? new StatusMessage(trimmed, lasting, PresenceDurations.EndOf(lasting, writtenAt, zone))
            : new StatusMessage(trimmed, Clears: null, ClearsAt: null);
    }

    /// <summary>Says whether the message is still shown at an instant.</summary>
    /// <param name="instant">The time of the read.</param>
    /// <returns><see langword="true" /> while it has not cleared.</returns>
    public bool IsInEffectAt(DateTimeOffset instant) => this.ClearsAt is not { } end || instant < end;
}
