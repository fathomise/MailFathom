// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Composes the statement that settles the named messages still settling and returns the ones it settled.</summary>
/// <remarks>
/// SQL rather than <c>ExecuteUpdate</c> because the caller needs the rows this statement changed rather than their count:
/// the condition on the column is what makes a competing run's settlement of the same message a no-op here, and only
/// the rows returned are announced.
/// </remarks>
internal static class MailSettlementStatement
{
    internal static FormattableString Compose(Guid[] identities, DateTimeOffset settledAt) =>
        $"""
         UPDATE stored_emails
         SET "SettledAt" = {settledAt}
         WHERE "Id" = ANY({identities})
           AND "SettledAt" IS NULL
         RETURNING "Id" AS "Value"
         """;
}
