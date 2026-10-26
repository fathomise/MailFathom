// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Writes the signer's key material one extraction found, inside the caller's open session.</summary>
/// <remarks>
/// <para>
/// Every writer of extracted metadata calls this beside the metadata mapping, so the key a row keeps is always the one
/// its own signature columns describe: a message re-derived as unsigned, or as signed by somebody else, loses the key an
/// earlier reading kept rather than holding on to it.
/// </para>
/// <para>
/// The write is idempotent from a fresh read, so two runs reaching one message at once converge: both find no row and
/// both insert, the loser violates the named primary key, and the optimistic retry re-reads the row the winner wrote.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal static class StoredEmailSignerKeyWriter
{
    /// <summary>Saves, replaces, or removes the key material of one stored email's signer.</summary>
    /// <param name="dbContext">The context whose transaction this write joins.</param>
    /// <param name="storedEmail">The email the key belongs to, tracked or already persisted.</param>
    /// <param name="signature">What verification established about the email's signature.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been issued or staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    public static async Task SaveAsync(
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        EmailSignature signature,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storedEmail);
        ArgumentNullException.ThrowIfNull(signature);

        var keyMaterial = signature.Signer is { KeyMaterial.IsEmpty: false } signer ? signer.KeyMaterial.ToArray() : null;

        // The change-tracker pass comes first for the reason the search document's does: a row staged earlier in this
        // same uncommitted session is invisible to a set-based statement, and acting on it twice would insert twice.
        var tracked = dbContext.StoredEmailSignerKeys.Local.SingleOrDefault(row => row.StoredEmailId == storedEmail.Id);
        if (tracked is not null)
        {
            if (keyMaterial is null)
            {
                dbContext.StoredEmailSignerKeys.Remove(tracked);
            }
            else
            {
                tracked.KeyMaterial = keyMaterial;
            }

            return;
        }

        var stored = dbContext.StoredEmailSignerKeys.Where(row => row.StoredEmailId == storedEmail.Id);
        if (keyMaterial is null)
        {
            await stored.ExecuteDeleteAsync(cancellationToken);

            return;
        }

        var updatedRowCount = await stored.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.KeyMaterial, keyMaterial),
            cancellationToken);

        if (updatedRowCount == 0)
        {
            dbContext.StoredEmailSignerKeys.Add(new StoredEmailSignerKeyEntity
            {
                StoredEmailId = storedEmail.Id,
                StoredEmail = storedEmail,
                KeyMaterial = keyMaterial,
            });
        }
    }
}
