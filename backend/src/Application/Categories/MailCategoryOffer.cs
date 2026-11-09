// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Categories;

namespace MailFathom.Application.Categories;

/// <summary>The categories one mail account shows a reader and lets a caller ask for.</summary>
/// <remarks>
/// A default the account switched off is left out rather than removed from what was recorded: it is hidden, so nothing
/// about the message changes and switching it back on costs nothing. An account that does not categorize offers
/// nothing, whatever an earlier setting recorded on its mail.
/// </remarks>
public sealed class MailCategoryOffer
{
    private MailCategoryOffer(IReadOnlyList<string> names) => this.Names = names;

    /// <summary>Gets the offer of an account that does not categorize, or that this deployment does not serve.</summary>
    public static MailCategoryOffer None { get; } = new([]);

    /// <summary>Gets every category offered, the defaults by their published identities and the account's own by their names.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Builds the offer of an account that categorizes.</summary>
    /// <param name="disabledDefaults">The identities of the defaults the account does not show.</param>
    /// <param name="ownCategoryNames">The names of the subjects the account named for itself.</param>
    /// <returns>The offer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static MailCategoryOffer Of(IEnumerable<string> disabledDefaults, IEnumerable<string> ownCategoryNames)
    {
        ArgumentNullException.ThrowIfNull(disabledDefaults);
        ArgumentNullException.ThrowIfNull(ownCategoryNames);

        var hidden = disabledDefaults.ToHashSet(StringComparer.Ordinal);

        return new(
        [
            .. DefaultMailCategory.All.Select(static category => category.ToString()).Where(identity => !hidden.Contains(identity)),
            .. ownCategoryNames,
        ]);
    }

    /// <summary>Finds the offered category a caller's text names.</summary>
    /// <param name="requested">The text, compared without regard to case.</param>
    /// <returns>The category as a stored record spells it, or <see langword="null" /> where nothing offered carries that name.</returns>
    /// <remarks>Without regard to case because an account's categories are unique whatever the case, so the text can only mean one of them.</remarks>
    public string? NameMatching(string requested) =>
        this.Names.FirstOrDefault(name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase));

    /// <summary>Narrows what a message or a conversation holds to what this account shows.</summary>
    /// <param name="recorded">The categories as the records spell them, compared exactly, in any order.</param>
    /// <returns>The categories shown, a default before an own category and the account's own as it declares them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recorded" /> is <see langword="null" />.</exception>
    /// <remarks>It walks what the account offers rather than what is recorded, so the order is the one a screen draws and an answer never exceeds what one account offers.</remarks>
    public IReadOnlyList<string> Shown(IReadOnlyCollection<string> recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        return [.. this.Names.Where(name => recorded.Contains(name, StringComparer.Ordinal))];
    }
}
