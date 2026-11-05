// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;

namespace MailFathom.Application.Rules;

/// <summary>Identifies one rule of one mail account for its whole life, whatever it is renamed to or moved to.</summary>
/// <remarks>
/// Minted once when the rule is created and never changed, so it is what a surface addresses a rule by, what a scheduled
/// walk's identity is composed from, and what a rule set's revision is derived over in place of the name. A name moves
/// when a person renames the rule and a position moves when a rule above it is removed, so neither can be what anything
/// holds on to. It is not a secret.
/// </remarks>
public readonly record struct MailRuleId
{
    private MailRuleId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Gets whether this value names a rule rather than the unusable struct default.</summary>
    public bool IsSpecified => this.Value != Guid.Empty;

    /// <summary>Restores a rule identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated rule identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailRuleId Create(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("A mail rule identifier cannot be empty.", nameof(value))
        : new MailRuleId(value);

    /// <summary>Mints the identifier of a rule being created now.</summary>
    /// <param name="now">The instant the rule is created at, which the version 7 identifier is ordered by.</param>
    /// <returns>A fresh identifier.</returns>
    public static MailRuleId Mint(DateTimeOffset now) => new(Guid.CreateVersion7(now));

    /// <summary>Reads a rule identifier written in its canonical hyphenated form.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="id">The identifier, or the unspecified default when the text names none.</param>
    /// <returns><see langword="true" /> when the text names a rule identifier.</returns>
    public static bool TryParse(string? value, out MailRuleId id)
    {
        if (Guid.TryParseExact(value?.Trim(), "D", out var parsed) && parsed != Guid.Empty)
        {
            id = new MailRuleId(parsed);

            return true;
        }

        id = default;

        return false;
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString("D", CultureInfo.InvariantCulture);
}
