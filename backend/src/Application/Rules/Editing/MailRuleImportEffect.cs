// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Editing;

/// <summary>What an import does to one rule.</summary>
public enum MailRuleImportEffect
{
    /// <summary>The account has no rule of that name, so the rule is created disabled.</summary>
    Added = 0,

    /// <summary>The account's rule of that name declares something else, and the import replaces what it declares.</summary>
    Changed = 1,

    /// <summary>The account's rule of that name declares the same, and the import puts it elsewhere among the rules both keep.</summary>
    Moved = 2,

    /// <summary>The account's rule of that name declares the same and keeps its place.</summary>
    Unchanged = 3,

    /// <summary>The account's rule is one a replacing import does not name, so it is removed.</summary>
    Removed = 4,
}
