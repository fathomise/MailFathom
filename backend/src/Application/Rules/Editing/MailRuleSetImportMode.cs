// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Editing;

/// <summary>What an import does to the rules an account already has.</summary>
/// <remarks>Both match an imported rule to a stored one by its name, without regard to case, so a matched rule keeps its identifier, its history, and whether it runs.</remarks>
public enum MailRuleSetImportMode
{
    /// <summary>The account's rules become exactly the imported ones, in the imported order; a stored rule the import does not name is removed.</summary>
    Replace = 0,

    /// <summary>A stored rule the import names is replaced where it stands, an imported rule naming none is added at the end, and every other stored rule is kept.</summary>
    Merge = 1,
}
