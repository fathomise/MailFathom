// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Sets;

/// <summary>One committed write to an account's rules: the whole set it produced, what it replaced, and who wrote it.</summary>
/// <param name="Account">The account the rules belong to.</param>
/// <param name="Version">The version the write produced.</param>
/// <param name="Revision">The revision the write produced.</param>
/// <param name="ReplacedRevision">The revision the set had before the write.</param>
/// <param name="Actor">The user who wrote it, or <see langword="null" /> once that user is erased or where no user did.</param>
/// <param name="Surface">The surface the write came through.</param>
/// <param name="CommittedAt">When the write committed.</param>
/// <param name="Declarations">Every rule of the set as it stood after the write, disabled ones and names included.</param>
/// <remarks>
/// Append-only, and read two ways: a past execution resolves the revision it ran under to the newest entry of its account
/// carrying that revision committed at or before the run, and a person reads the entries as the record of who changed
/// what. It is never updated except by the actor's erasure, which clears <paramref name="Actor" /> and keeps the rest.
/// </remarks>
public sealed record MailRuleSetArchiveEntry(
    MailAccountId Account,
    long Version,
    MailRuleSetRevision Revision,
    MailRuleSetRevision ReplacedRevision,
    UserId? Actor,
    MailRuleChangeSurface Surface,
    DateTimeOffset CommittedAt,
    IReadOnlyList<MailRuleDeclaration> Declarations);
