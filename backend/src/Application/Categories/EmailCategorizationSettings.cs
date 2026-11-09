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
}
