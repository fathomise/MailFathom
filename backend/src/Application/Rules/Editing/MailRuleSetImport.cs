// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Editing;

/// <summary>A whole set of rules to bring into one account, and whether it replaces the account's rules or merges into them.</summary>
/// <param name="Mode">Whether the set replaces the account's rules or merges into them.</param>
/// <param name="Rules">The rules, in the order they are imported in; a name may appear at most once.</param>
/// <remarks>
/// A rule carries no enabled state. An imported rule the account does not have yet is a create, and commits disabled
/// like every other, which
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0048-keeping-a-mail-accounts-rules-in-postgresql-and-changing-them-at-runtime.md">ADR 0048</see>
/// decides; a rule replacing one the account has keeps whether that one runs, as an edit does.
/// </remarks>
public sealed record MailRuleSetImport(MailRuleSetImportMode Mode, IReadOnlyList<MailRuleDraft> Rules);
