// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Tasks;

/// <summary>Names the person on the other side of a task: who it is owed to, or who owes it.</summary>
/// <remarks>
/// Held as an address and the name it was written under rather than as a reference to a contact, so the task keeps
/// saying who it meant after the mail that introduced them is gone.
/// </remarks>
public sealed record TaskCounterpart
{
    /// <summary>The longest display name stored.</summary>
    public const int MaximumDisplayNameLength = 200;

    /// <summary>The longest address stored, which is the most an address column holds anywhere in this system.</summary>
    public const int MaximumAddressLength = 320;

    /// <summary>Names a counterpart.</summary>
    /// <param name="address">The address the counterpart is reached at.</param>
    /// <param name="displayName">The name to draw them with, or <see langword="null" /> where there is none. Surrounding whitespace is trimmed and a blank name is no name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="address" /> is the struct default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="address" /> exceeds <see cref="MaximumAddressLength" />, or the trimmed <paramref name="displayName" /> exceeds <see cref="MaximumDisplayNameLength" />.</exception>
    public TaskCounterpart(EmailAddress address, string? displayName)
    {
        if (address.NormalizedAddress is null)
        {
            throw new ArgumentException("A counterpart is reached at a specified address.", nameof(address));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(address.Address.Length, MaximumAddressLength, nameof(address));

        var trimmed = displayName?.Trim();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed?.Length ?? 0, MaximumDisplayNameLength, nameof(displayName));

        this.Address = address;
        this.DisplayName = string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Gets the address the counterpart is reached at.</summary>
    public EmailAddress Address { get; }

    /// <summary>Gets the trimmed name to draw them with, and <see langword="null" /> where there is none.</summary>
    public string? DisplayName { get; }
}
