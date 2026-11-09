// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Categories;

/// <summary>The bounds a mailbox's own categories are held to, wherever they are written or stored.</summary>
/// <remarks>Every own category costs a question on every message, which is what the limit on how many an account holds bounds.</remarks>
public static class OwnMailCategoryBounds
{
    /// <summary>The most own categories one account holds.</summary>
    public const int MaximumPerAccount = 20;

    /// <summary>The greatest length of an own category's name.</summary>
    public const int MaximumNameLength = 64;

    /// <summary>The greatest length of the description a model reads an own category by.</summary>
    public const int MaximumDescriptionLength = 500;
}
