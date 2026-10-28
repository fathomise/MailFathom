// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails.Authentication;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Resets the stored trust verdicts a removed contact recognized, before the contact goes.</summary>
/// <remarks>
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0043-how-a-sender-is-verified-and-trusted-and-contacts-as-the-one-trusted-sender-list.md">ADR 0043</see>
/// has every act that removes a contact — erasing it, erasing the user whose book holds it, deleting the organization
/// whose book holds it — reset each verdict naming it to the not-evaluated verdict, in that act's own transaction and
/// before any cascade. So a data-subject erasure is never blocked by a verdict, and no trusted mark is left naming a
/// contact that is gone. Each caller has already taken the address lock for every address the removed book held.
/// </remarks>
[RequiresIntegrationCoverage]
internal static class ContactTrustVerdicts
{
    /// <summary>Resets every verdict naming one of the contacts to the not-evaluated verdict.</summary>
    /// <param name="context">The context whose open transaction the reset joins.</param>
    /// <param name="contactIds">The contacts being removed, as a query the database evaluates.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes once the statement ran.</returns>
    internal static Task ResetNamingAsync(
        MailFathomDbContext context,
        IQueryable<Guid> contactIds,
        CancellationToken cancellationToken) =>
        context.StoredEmails
            .Where(email => email.SenderTrustContactId != null && contactIds.Contains(email.SenderTrustContactId.Value))
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(email => email.SenderTrustLevel, SenderTrustLevel.Unknown)
                    .SetProperty(email => email.SenderTrustGrantedBy, SenderTrustSource.None)
                    .SetProperty(email => email.SenderTrustAuthorEvidence, TrustedAuthorEvidence.None)
                    .SetProperty(email => email.SenderTrustPolicyRevision, (string?)null)
                    .SetProperty(email => email.SenderTrustOrganizationId, (Guid?)null)
                    .SetProperty(email => email.SenderTrustContactId, (Guid?)null),
                cancellationToken);
}
