// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>What erasing one user removed.</summary>
/// <param name="UserErased">Whether a user record was there to remove, so a repeat is reported as the no-op it is.</param>
/// <param name="RowsErasedBesideTheCascade">
/// How many rows the seam took or rewrote itself rather than leaving to the cascade from the user row: what the user
/// authored in a mail account, and the rule revisions that stop naming them.
/// </param>
internal readonly record struct UserErasure(bool UserErased, int RowsErasedBesideTheCascade);
