// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Categories;

/// <summary>Names what said a message holds a category, which a reader weighs before what it said.</summary>
public enum EmailCategorySource
{
    /// <summary>A deterministic rule of this deployment's own, named by the rule.</summary>
    DeterministicRule = 0,

    /// <summary>A decision model, named by the build that answered.</summary>
    Model = 1,
}
