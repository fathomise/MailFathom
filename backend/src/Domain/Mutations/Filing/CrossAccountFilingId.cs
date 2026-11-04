// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Mutations.Filing;

/// <summary>Identifies the append half of one message filed into another account's folder.</summary>
public readonly record struct CrossAccountFilingId
{
    private CrossAccountFilingId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a filing identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated filing identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static CrossAccountFilingId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A cross-account filing identifier cannot be empty.", nameof(value));
        }

        return new CrossAccountFilingId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
