// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Turns one contact into the rows that keep it, and back.</summary>
/// <remarks>
/// <para>
/// The domain type is what the invariants live in, so this mapping never repairs a row it disagrees with: rebuilding a
/// contact goes through <see cref="Contact.Create" /> exactly as a write does, and a stored shape the domain refuses
/// fails here rather than becoming a record no writer could have produced.
/// </para>
/// <para>
/// Whose book a contact is written into travels beside the contact rather than on it. A contact is the person and what
/// was recorded about them, and which book that record sits in is the store's question — resolved once per act and
/// handed down — so the domain type stays free of a column that names a caller's own scope.
/// </para>
/// <para>
/// The holder decides four columns rather than one: the key the row and its addresses are filed under, and whichever
/// of the user, the organization, and the mail account the book belongs to. They are written together here so the check constraint
/// beneath them has nothing to refuse.
/// </para>
/// </remarks>
internal static class ContactMapping
{
    /// <summary>Builds the rows one contact is kept as.</summary>
    /// <param name="holder">The book the contact is written into.</param>
    /// <param name="contact">The contact to keep.</param>
    /// <param name="writer">Who wrote the contact, which every one of its addresses records.</param>
    /// <returns>The row to insert, with its address rows already attached.</returns>
    /// <exception cref="ArgumentException">Thrown when the contact's origin is not the one that book holds.</exception>
    internal static ContactEntity ToEntity(ContactBookHolder holder, Contact contact, ContactAddressWriter writer)
    {
        if (contact.Origin != holder.Origin)
        {
            throw new ArgumentException(
                "A contact is written into the book that holds its origin and no other.",
                nameof(contact));
        }

        var entity = new ContactEntity
        {
            Id = contact.Id.Value,
            BookHolderId = holder.Key,
            UserId = holder.User?.Value,
            OrganizationId = holder.Organization,
            MailboxAccountId = holder.Account?.Value,
            DisplayName = contact.DisplayName.Value,
            DisplayNameSortKey = contact.DisplayName.SortKey,
            PreferredNormalizedAddress = contact.PreferredAddress.NormalizedAddress,
            Note = contact.Note?.Value,
            Origin = contact.Origin,
            RecordedAt = contact.RecordedAt,
            AmendedAt = contact.AmendedAt,
        };

        foreach (var address in contact.Addresses)
        {
            entity.Addresses.Add(ToAddressEntity(holder, contact, address, writer));
        }

        for (var position = 0; position < contact.PhoneNumbers.Count; position++)
        {
            entity.PhoneNumbers.Add(ToPhoneNumberEntity(contact, contact.PhoneNumbers[position], position));
        }

        return entity;
    }

    /// <summary>Builds the row one of a contact's phone numbers is kept as.</summary>
    /// <param name="contact">The contact the number belongs to.</param>
    /// <param name="phoneNumber">The number to keep.</param>
    /// <param name="position">Its place in the contact's order.</param>
    /// <returns>The phone number row.</returns>
    internal static ContactPhoneNumberEntity ToPhoneNumberEntity(
        Contact contact,
        ContactPhoneNumber phoneNumber,
        int position) =>
        new()
        {
            ContactId = contact.Id.Value,
            Position = position,
            Number = phoneNumber.Number,
            Label = phoneNumber.Label,
        };

    /// <summary>Builds the row one of a contact's addresses is kept as.</summary>
    /// <param name="holder">The book the contact is in, which the row repeats because the uniqueness rule spans this table alone.</param>
    /// <param name="contact">The contact the address belongs to.</param>
    /// <param name="address">The address to keep.</param>
    /// <param name="writer">Who wrote the address, stamped with the contact's time of this write.</param>
    /// <returns>The address row.</returns>
    internal static ContactAddressEntity ToAddressEntity(
        ContactBookHolder holder,
        Contact contact,
        EmailAddress address,
        ContactAddressWriter writer) =>
        new()
        {
            // Version 7 over the instant this record was written rather than a random value, so address rows are
            // clustered by insertion the way every other identifier this system mints is. The write time is what the
            // amendment stamped, which for a contact being recorded is its arrival and for one gaining an address later
            // is that later moment — taking the arrival in both cases would file today's row under a year-old key.
            // Nothing reads the ordering between them.
            Id = Guid.CreateVersion7(contact.AmendedAt),
            ContactId = contact.Id.Value,
            BookHolderId = holder.Key,
            Address = address.Address,
            NormalizedAddress = address.NormalizedAddress,
            WriterKind = writer.Kind,
            WriterUserId = writer.User?.Value,
            WriterCredentialId = writer.Credential,
            WrittenAt = contact.AmendedAt,
        };

    /// <summary>Rebuilds the contact one stored row states.</summary>
    /// <param name="entity">The stored row, with its addresses loaded.</param>
    /// <returns>The contact that row states.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored rows do not form a contact the domain admits — no address, or a preferred address the contact does not hold.</exception>
    /// <remarks>
    /// A row naming a preferred address that is not among the contact's cannot be repaired by picking another, because
    /// which address a person's mail goes to by default is the user's choice and inventing it would send mail somewhere
    /// nobody chose. It is the one part of the record no constraint can hold, which is why it is refused here.
    /// </remarks>
    internal static Contact ToContact(ContactEntity entity)
    {
        var addresses = entity.Addresses
            .Select(ToAddress)
            .ToArray();

        var preferred = addresses
            .Where(address => string.Equals(
                address.NormalizedAddress,
                entity.PreferredNormalizedAddress,
                StringComparison.Ordinal))
            .ToArray();

        if (preferred.Length != 1)
        {
            throw new ArgumentException("A stored contact names one preferred address it holds.", nameof(entity));
        }

        return Contact.Create(
            ContactId.Create(entity.Id),
            ContactDisplayName.Create(entity.DisplayName),
            addresses,
            preferred[0],
            entity.Note is null ? null : ContactNote.Create(entity.Note),
            entity.Origin,
            entity.RecordedAt,
            entity.AmendedAt,
            entity.OrganizationId,
            entity.PhotoFileId,
            [.. entity.PhoneNumbers
                .OrderBy(phoneNumber => phoneNumber.Position)
                .Select(phoneNumber => ContactPhoneNumber.Create(phoneNumber.Number, phoneNumber.Label))]);
    }

    /// <summary>Loads every row <see cref="ToContact" /> rebuilds a contact from, beside the contact's own.</summary>
    /// <param name="contacts">The contacts a query reads.</param>
    /// <returns>The same query with the addresses and the phone numbers loaded in one statement.</returns>
    /// <remarks>
    /// One statement rather than one per collection, because the preferred address is read against the addresses and a
    /// split read could see an amendment between the two. The product of a person's addresses and numbers is bounded
    /// by the two ceilings the domain holds.
    /// </remarks>
    internal static IQueryable<ContactEntity> WithRecordRows(IQueryable<ContactEntity> contacts) =>
        contacts
            .Include(record => record.Addresses)
            .Include(record => record.PhoneNumbers)
            .AsSingleQuery();

    /// <summary>Rebuilds one stored address, which the write path already validated.</summary>
    private static EmailAddress ToAddress(ContactAddressEntity entity) =>
        EmailAddress.TryCreate(displayName: null, entity.Address, out var address)
            ? address
            : throw new ArgumentException("A stored contact address is not a usable address.", nameof(entity));
}
