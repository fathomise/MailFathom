// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;

namespace MailFathom.Domain.Folders;

/// <summary>Identifies a folder of any mail account, for the folder's whole life.</summary>
/// <remarks>
/// Renaming or moving the folder, the server renaming it, a new resolution of its source binding, a drain into
/// <c>Held</c>, and a restore back to <c>Mirrored</c> change nothing about it, which is why a stored message, every
/// surface, and every folder-scoped query refer to it rather than to a path or an alias. It is not a secret: whether a
/// caller may read the folder is the account's authorization, never knowledge of the identifier. See
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>.
/// </remarks>
public readonly record struct MailFolderId
{
    private MailFolderId(Guid value) => this.Value = value;

    /// <summary>Gets the identifier.</summary>
    public Guid Value { get; }

    /// <summary>Wraps a stored identifier.</summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The folder identity.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailFolderId Create(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A folder is identified by a non-empty identifier.", nameof(value))
            : new MailFolderId(value);

    /// <summary>Mints the identifier of a folder that exists in MailFathom for the first time.</summary>
    /// <param name="now">The instant the folder first exists, which a version 7 identifier carries.</param>
    /// <returns>A new folder identity.</returns>
    public static MailFolderId Mint(DateTimeOffset now) => new(Guid.CreateVersion7(now));

    /// <summary>Reads a folder identifier a caller wrote, in the form <see cref="ToString" /> produces.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="id">The identifier, when the text is one.</param>
    /// <returns><see langword="true" /> when the text names a non-empty identifier.</returns>
    public static bool TryParse(string? value, out MailFolderId id)
    {
        if (Guid.TryParseExact(value?.Trim(), "D", out var parsed) && parsed != Guid.Empty)
        {
            id = new MailFolderId(parsed);

            return true;
        }

        id = default;

        return false;
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString("D", CultureInfo.InvariantCulture);
}
