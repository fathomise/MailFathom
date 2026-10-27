// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.TestSupport;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Builds the local filer of a deployment whose accounts are all mirrored, which is every test not about a held account.</summary>
internal static class LocalMailFilers
{
    /// <summary>Builds a filer whose resolver finds no account held, so it files nothing.</summary>
    /// <param name="clock">The clock the filer mints with, which it never reaches.</param>
    /// <returns>The filer.</returns>
    internal static LocalMailFiler HoldingNothing(TimeProvider clock) => new MailboxCustodianComposition(clock).LocalFiler;
}
