// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Categories;

/// <summary>What one account decided about sorting its mail into categories, as far as asking a model is concerned.</summary>
/// <remarks>
/// The defaults an account switched off are not here: a default switched off is hidden when it is read, and the question
/// stays the whole set so that switching one back on costs nothing.
/// </remarks>
/// <param name="IsEnabled">Whether the account's mail is sorted into categories at all.</param>
/// <param name="OwnCategories">The subjects the account names for itself.</param>
public sealed record EmailCategorizationSettings(bool IsEnabled, IReadOnlyList<OwnMailCategory> OwnCategories)
{
    /// <summary>Gets the settings of an account that sorts nothing.</summary>
    public static EmailCategorizationSettings Disabled { get; } = new(IsEnabled: false, []);

    /// <summary>Finds the own category a name means.</summary>
    /// <param name="name">The name, compared without regard to case because an account's categories are unique whatever the case.</param>
    /// <returns>The category, or <see langword="null" /> where the account names none so.</returns>
    public OwnMailCategory? OwnCategoryNamed(string name) =>
        this.OwnCategories.FirstOrDefault(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase));
}
