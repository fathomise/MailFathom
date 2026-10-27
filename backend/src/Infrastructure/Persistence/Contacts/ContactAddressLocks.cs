// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Takes the transaction-scoped lock every change to a contact book and every trust verdict serialize on, one per address.</summary>
/// <remarks>
/// <para>
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0043-how-a-sender-is-verified-and-trusted-and-contacts-as-the-one-trusted-sender-list.md">ADR 0043</see>
/// keys it by the address alone, in its comparison form, rather than by the book: one verdict reads several books, so a
/// lock per book would let a write to one pass a reader of another. A transaction that changes a book takes it for each
/// address it changes, so a reader taking it for an address before reading any book sees the books wholly before a
/// write or wholly after it.
/// </para>
/// <para>
/// The keys are taken in sorted order within one statement — PostgreSQL evaluates a volatile function in the target list
/// after the sort — so two transactions needing several of them never deadlock. A collision between two addresses'
/// hashes only serializes work that needed no serializing. The seed keeps these keys clear of every other advisory
/// lock the product takes.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal static class ContactAddressLocks
{
    /// <summary>The seed each address's key is derived with, so these locks meet no other store's.</summary>
    internal const long AddressSerializationSeed = 4_310_772_918_664_105_213L;

    /// <summary>Takes the lock for each address, in sorted key order, inside the transaction the context is enlisted in.</summary>
    /// <param name="context">The context whose open transaction holds the locks until it ends.</param>
    /// <param name="normalizedAddresses">The addresses, in their comparison form.</param>
    /// <param name="cancellationToken">Cancels the wait for a lock another transaction holds.</param>
    /// <returns>A task that completes once every lock is held.</returns>
    internal static async Task TakeAsync(
        MailFathomDbContext context,
        IEnumerable<string> normalizedAddresses,
        CancellationToken cancellationToken)
    {
        string[] addresses = [.. normalizedAddresses.Distinct(StringComparer.Ordinal)];

        if (addresses.Length == 0)
        {
            return;
        }

        await context.Database.ExecuteSqlAsync(
            $"""
             SELECT pg_advisory_xact_lock(held.key)
             FROM (SELECT DISTINCT hashtextextended(address, {AddressSerializationSeed}) AS key
                   FROM unnest({addresses}) AS address) AS held
             ORDER BY held.key
             """,
            cancellationToken);
    }
}
