// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>Selects the mail recorded under one category, in the accounts that offer it.</summary>
/// <param name="ComparableName">The name as a caller's cursor identifies it, upper-cased so two spellings are one walk.</param>
/// <param name="StoredNames">The name as the offering accounts' records spell it, which own categories in two accounts may do differently.</param>
/// <param name="Accounts">The accounts in scope that offer it.</param>
/// <remarks>
/// It constrains and never ranks: the predicate reads it, and no chunk, embedding, or score does. The accounts are held
/// here rather than written into <see cref="MailboxEmailSelection.CanonicalText" />, because which accounts offer a name
/// is configuration, and a cursor must not stop working because a setting moved during a walk.
/// </remarks>
public sealed record EmailCategorySelection(
    string ComparableName,
    IReadOnlyList<string> StoredNames,
    IReadOnlyList<MailAccountId> Accounts);
