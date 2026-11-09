// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Categories;

/// <summary>Says which of the two kinds of question a recorded category answers.</summary>
public enum EmailCategoryKind
{
    /// <summary>The one choice over <see cref="DefaultMailCategory.All" />, of which a message holds at most one.</summary>
    Default = 0,

    /// <summary>A yes-or-no question about a subject the account named, of which a message holds any number.</summary>
    Own = 1,
}
