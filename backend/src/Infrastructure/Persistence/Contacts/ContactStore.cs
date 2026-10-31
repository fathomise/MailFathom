// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Persistence;
using MailFathom.Application.StoredFiles;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Keeps the contact books in PostgreSQL, one book at a time.</summary>
/// <remarks>
/// <para>
/// Every operation here writes through the context enlisted in the caller's session, erasure included, so nothing can
/// land outside the transaction its caller opened. Nothing logs: every column but the identity and the origin is
/// personal data about a third party, so what a failure carries is the identifier and what a caller learns is the
/// outcome it was handed.
/// </para>
/// <para>
/// Which contact may hold an address is enforced by the unique index over the book and the comparison form rather than
/// by a read before the insert. Two callers claiming one address both read nothing, so only the constraint closes that
/// window — and losing it is a race the retry above resolves into the answer that names the holder.
/// </para>
/// <para>
/// Every statement here carries the book being written, beside the identity it was given. A contact identifier that
/// belongs to another book therefore matches no row rather than reaching one, so a replacement and an erasure are as
/// scoped as a read is, and neither can be aimed across books by a caller that learned an identifier elsewhere. The
/// erasure is stated over a whole scope rather than one book, because a person asking to be taken out is asking about
/// everything the user in front of them can see.
/// </para>
/// <para>
/// Recording, replacing, and erasing one contact each take <see cref="ContactAddressLocks" /> for every address the act
/// changes, inside the caller's transaction, so a reader serializing on an address sees the book wholly before the write
/// or wholly after it. Erasing a mailbox's whole collected book does not: a collected contact grants no trust, so no
/// trust verdict reads that book, and locking every address a mailbox ever corresponded with would hold an unbounded
/// number of locks in one transaction.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactStore : IContactStore
{
    /// <inheritdoc />
    public async Task AddAsync(
        IPersistenceSession session,
        ContactBookHolder holder,
        Contact contact,
        ContactAddressWriter writer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(writer);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        await ContactAddressLocks.TakeAsync(
            writeContext,
            contact.Addresses.Select(address => address.NormalizedAddress),
            cancellationToken);

        writeContext.Contacts.Add(ContactMapping.ToEntity(holder, contact, writer));
    }

    /// <inheritdoc />
    public async Task<bool> ReplaceAsync(
        IPersistenceSession session,
        ContactBookHolder holder,
        Contact contact,
        ContactAddressWriter writer,
        IReadOnlyCollection<EmailAddress> restatedAddresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(restatedAddresses);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var bookHolderId = holder.Key;

        // Tracked rather than projected, so the amendment is applied to the row as it stands now and the concurrency
        // token travels with it: a contact erased between this read and the commit makes the write affect no row, which
        // the session reports as a conflict rather than reinserting the person.
        var held = await ContactMapping.WithRecordRows(writeContext.Contacts)
            .FirstOrDefaultAsync(
                record => record.Id == contact.Id.Value && record.BookHolderId == bookHolderId,
                cancellationToken);

        if (held is null)
        {
            return false;
        }

        // Every address the record held or will hold, which covers each one the amendment adds, removes, or restates:
        // locking the ones it keeps as well only serializes the amendment against readers of an address whose standing
        // it does not change.
        await ContactAddressLocks.TakeAsync(
            writeContext,
            held.Addresses
                .Select(address => address.NormalizedAddress)
                .Concat(contact.Addresses.Select(address => address.NormalizedAddress)),
            cancellationToken);

        held.DisplayName = contact.DisplayName.Value;
        held.DisplayNameSortKey = contact.DisplayName.SortKey;
        held.PreferredNormalizedAddress = contact.PreferredAddress.NormalizedAddress;
        held.Note = contact.Note?.Value;
        held.AmendedAt = contact.AmendedAt;

        ReplaceAddresses(writeContext, held, holder, contact, writer, restatedAddresses);
        ReplacePhoneNumbers(writeContext, held, contact);

        return true;
    }

    /// <inheritdoc />
    public async Task<ContactPhotoRelinking> RelinkPhotoAsync(
        IPersistenceSession session,
        UserId user,
        ContactId contactId,
        StoredFileId? photo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var bookHolderId = ContactBookHolder.Of(user).Key;
        var contactValue = contactId.Value;

        // Tracked so the concurrency token travels with the write: the file read here as the one displaced is the file
        // the row still links when the commit lands, or the commit is a conflict the retry reads again.
        var held = await writeContext.Contacts.FirstOrDefaultAsync(
            record => record.Id == contactValue && record.BookHolderId == bookHolderId,
            cancellationToken);

        if (held is null)
        {
            return ContactPhotoRelinking.NoSuchContact;
        }

        var replaced = held.PhotoFileId is { } linked ? StoredFileId.Create(linked) : (StoredFileId?)null;

        held.PhotoFileId = photo?.Value;

        return new ContactPhotoRelinking(ContactHeld: true, replaced);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The photo goes with the person in the same transaction rather than waiting for the sweep: an erasure answered to a
    /// data subject has to be true when it is answered, so the file's row is deleted here and its object, where it has
    /// one, is released on the commit.
    /// </remarks>
    public async Task<ContactErasure> EraseAsync(
        IPersistenceSession session,
        ContactBookScope scope,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scope);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var contactValue = contactId.Value;
        var books = scope.Keys.ToArray();

        var heldAddresses = await writeContext.ContactAddresses
            .Where(address => address.ContactId == contactValue && books.Contains(address.BookHolderId))
            .Select(address => address.NormalizedAddress)
            .ToArrayAsync(cancellationToken);

        await ContactAddressLocks.TakeAsync(writeContext, heldAddresses, cancellationToken);

        await ContactTrustVerdicts.ResetNamingAsync(
            writeContext,
            writeContext.Contacts
                .Where(record => record.Id == contactValue && books.Contains(record.BookHolderId))
                .Select(record => record.Id),
            cancellationToken);

        // Deleted rather than counted and then cascaded, so the number reported is the number of rows this statement
        // removed. Counting first would report what a separate statement saw, which a write committed between the two
        // makes a different set. The foreign key still cascades and is still what guarantees no address outlives its
        // person; this only takes the same rows first, inside the caller's transaction, to be able to report them.
        var erasedAddresses = await writeContext.ContactAddresses
            .Where(address => address.ContactId == contactValue && books.Contains(address.BookHolderId))
            .ExecuteDeleteAsync(cancellationToken);

        var photos = await writeContext.Contacts
            .Where(record => record.Id == contactValue && books.Contains(record.BookHolderId) && record.PhotoFileId != null)
            .Select(record => record.PhotoFileId!.Value)
            .ToArrayAsync(cancellationToken);

        if (photos.Length > 0)
        {
            await ReleasedContentObjects.ReleaseForStoredFilesAsync(session, photos, cancellationToken);

            await writeContext.StoredFiles
                .Where(file => photos.Contains(file.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var erasedContacts = await writeContext.Contacts
            .Where(record => record.Id == contactValue && books.Contains(record.BookHolderId))
            .ExecuteDeleteAsync(cancellationToken);

        return new ContactErasure(contactId, erasedContacts > 0, erasedAddresses);
    }

    /// <inheritdoc />
    public async Task<CollectedContactErasure> EraseCollectedAsync(
        IPersistenceSession session,
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var bookHolderId = ContactBookHolder.Of(account).Key;

        // The addresses go first, and by their book rather than by a list of identifiers this method read: a set-based
        // delete keeps a book of collected people out of memory, and taking the rows in the same order and the same
        // transaction the single-contact erasure does means the counts reported are the rows removed. Nothing narrows
        // by origin, because a mail account's book holds one origin by construction.
        var erasedAddresses = await writeContext.ContactAddresses
            .Where(address => address.BookHolderId == bookHolderId)
            .ExecuteDeleteAsync(cancellationToken);

        var erasedContacts = await writeContext.Contacts
            .Where(record => record.BookHolderId == bookHolderId)
            .ExecuteDeleteAsync(cancellationToken);

        return new CollectedContactErasure(erasedContacts, erasedAddresses);
    }

    /// <summary>Brings the stored address rows to exactly the set the amended contact names.</summary>
    /// <remarks>
    /// An address the record still names keeps its row, and with it the writer who stated it, so an amendment that only
    /// changed the name beside it changes neither who stated the address nor whether it grants trust — unless the
    /// writer restated it, which records them as its writer in place of whoever was. An address the amendment adds
    /// records the amendment's writer. What was dropped is deleted rather than left orphaned, which is also what frees
    /// the address for another contact to claim.
    /// </remarks>
    internal static void ReplaceAddresses(
        MailFathomDbContext writeContext,
        ContactEntity held,
        ContactBookHolder holder,
        Contact contact,
        ContactAddressWriter writer,
        IReadOnlyCollection<EmailAddress> restatedAddresses)
    {
        var named = contact.Addresses.ToDictionary(
            address => address.NormalizedAddress,
            StringComparer.Ordinal);
        var restated = restatedAddresses.Select(address => address.NormalizedAddress).ToHashSet(StringComparer.Ordinal);

        foreach (var stored in held.Addresses.ToArray())
        {
            if (named.Remove(stored.NormalizedAddress, out var address))
            {
                stored.Address = address.Address;

                if (restated.Contains(stored.NormalizedAddress))
                {
                    Restate(stored, writer, contact.AmendedAt);
                }
            }
            else
            {
                held.Addresses.Remove(stored);
                writeContext.ContactAddresses.Remove(stored);
            }
        }

        foreach (var added in named.Values)
        {
            held.Addresses.Add(ContactMapping.ToAddressEntity(holder, contact, added, writer));
        }
    }

    /// <summary>Brings the stored phone number rows to exactly the numbers the amended contact holds, in its order.</summary>
    /// <remarks>
    /// A position both sides hold is rewritten in place rather than deleted and inserted, because the position is part
    /// of the key and the change tracker refuses a second row under a key it is about to delete.
    /// </remarks>
    internal static void ReplacePhoneNumbers(MailFathomDbContext writeContext, ContactEntity held, Contact contact)
    {
        var stored = held.PhoneNumbers.ToDictionary(row => row.Position);

        foreach (var dropped in stored.Values.Where(row => row.Position >= contact.PhoneNumbers.Count))
        {
            held.PhoneNumbers.Remove(dropped);
            writeContext.ContactPhoneNumbers.Remove(dropped);
        }

        for (var position = 0; position < contact.PhoneNumbers.Count; position++)
        {
            var phoneNumber = contact.PhoneNumbers[position];

            if (stored.TryGetValue(position, out var row))
            {
                row.Number = phoneNumber.Number;
                row.Label = phoneNumber.Label;
            }
            else
            {
                held.PhoneNumbers.Add(ContactMapping.ToPhoneNumberEntity(contact, phoneNumber, position));
            }
        }
    }

    /// <summary>Records a kept address as written by the amendment's writer, at the amendment's time.</summary>
    private static void Restate(ContactAddressEntity stored, ContactAddressWriter writer, DateTimeOffset amendedAt)
    {
        stored.WriterKind = writer.Kind;
        stored.WriterUserId = writer.User?.Value;
        stored.WriterCredentialId = writer.Credential;
        stored.WrittenAt = amendedAt;
    }
}
