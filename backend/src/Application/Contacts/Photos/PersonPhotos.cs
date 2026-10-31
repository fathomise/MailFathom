// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts.Members;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts.Photos;

/// <summary>Answers, for the addresses one read draws, which photo each person at them is drawn by for the caller.</summary>
/// <remarks>
/// <para>
/// <b>The order is the one a person is shown in everywhere else</b>, which
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0044-an-organizations-member-directory-as-a-projection-of-user-records.md">ADR 0044</see>
/// § <em>Where it sits</em> states: the reader's own book, then the member directory, then the organization's contacts,
/// then the collected books. Only the first two ever carry a picture, so an address is answered by the first of those two
/// holding it and by nothing after them. The first book holding an address answers for it whether or not it carries a
/// photo: a colleague the reader wrote down without one is drawn by their initials, as their contact on the People
/// screen is, rather than by a portrait the reader's own record of them hides.
/// </para>
/// <para>
/// <b>A photo is only ever named to somebody who could read it.</b> The routes serving both kinds take
/// <see cref="MailFathomPermission.ContactsRead" />, so a caller without it is answered with nothing rather than refused:
/// the read that drew the addresses is theirs, and the initials are what they see instead. A member's portrait is named
/// only to a member of the same organization, because the directory read is the reader's own organization's, and the
/// reader is left out of it as they are out of their colleagues.
/// </para>
/// <para>
/// A reference is answered rather than a picture, and the addresses of a whole read are answered together: one read of
/// the scope, a handful of address lookups in the reader's own book, and at most one read of the directory — never one
/// request per person drawn.
/// </para>
/// <para>Nothing here logs: an address and whose photo it is are personal data about a third party.</para>
/// </remarks>
public sealed class PersonPhotos
{
    /// <summary>The most distinct addresses one resolution answers, which is one page of the largest list the client draws.</summary>
    public const int MaximumAddresses = ContactQuery.MaximumPageSize;

    private static readonly IReadOnlyDictionary<EmailAddress, PersonPhoto> None = new Dictionary<EmailAddress, PersonPhoto>();

    private readonly IContactDirectory directory;
    private readonly IMemberDirectory members;
    private readonly ContactBookOwnership ownership;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the resolution.</summary>
    /// <param name="directory">Answers which contacts of the reader's own book hold an address, and what they carry.</param>
    /// <param name="members">Reads the reader's organization's member directory.</param>
    /// <param name="ownership">Answers whose books this caller reads, and which organization they belong to.</param>
    /// <param name="authorization">Answers which grants the caller holds.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public PersonPhotos(
        IContactDirectory directory,
        IMemberDirectory members,
        ContactBookOwnership ownership,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(authorization);

        this.directory = directory;
        this.members = members;
        this.ownership = ownership;
        this.authorization = authorization;
    }

    /// <summary>Resolves the photo each address is drawn by.</summary>
    /// <param name="addresses">The addresses one read draws, in any order and with repeats.</param>
    /// <param name="cancellationToken">Cancels the reads of the caller's scope, book, and directory.</param>
    /// <returns>An entry for every address drawn by a photo, keyed by the address; one drawn by initials is absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="addresses" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more than <see cref="MaximumAddresses" /> distinct addresses are supplied.</exception>
    public async Task<IReadOnlyDictionary<EmailAddress, PersonPhoto>> ResolveAsync(
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        EmailAddress[] asked = [.. addresses.Distinct()];
        ArgumentOutOfRangeException.ThrowIfGreaterThan(asked.Length, MaximumAddresses, nameof(addresses));

        if (asked.Length == 0 || !this.authorization.Permits(MailFathomPermission.ContactsRead))
        {
            return None;
        }

        var scope = await this.ownership.ReadScopeAsync(cancellationToken);
        var ownContacts = await this.OwnContactsHoldingAsync(scope, asked, cancellationToken);
        var photos = ownContacts
            .Where(held => held.Value.Photo is not null)
            .ToDictionary(
                held => held.Key,
                held => new PersonPhoto(PersonPhotoSource.OwnContact, held.Value.Id.Value, held.Value.Photo!.Value));

        EmailAddress[] unheld = [.. asked.Where(address => !ownContacts.ContainsKey(address))];

        if (unheld.Length > 0 && scope.OrganizationBook?.Organization is { } organization)
        {
            foreach (var (address, photo) in await this.MemberPortraitsAsync(organization, scope.User, unheld, cancellationToken))
            {
                photos[address] = photo;
            }
        }

        return photos;
    }

    /// <summary>Reads the contact of the reader's own book holding each address, in lookups no larger than one contact's addresses.</summary>
    private async Task<Dictionary<EmailAddress, Contact>> OwnContactsHoldingAsync(
        ContactBookScope scope,
        EmailAddress[] addresses,
        CancellationToken cancellationToken)
    {
        var holders = new Dictionary<EmailAddress, ContactId>();

        foreach (var lookup in addresses.Chunk(Contact.MaximumAddressCount))
        {
            foreach (var (address, contact) in await this.directory.FindHoldersOfAsync(scope.OwnBook, lookup, cancellationToken))
            {
                holders[address] = contact;
            }
        }

        if (holders.Count == 0)
        {
            return [];
        }

        var contacts = await this.directory.FindAllAsync(scope, [.. holders.Values.Distinct()], cancellationToken);

        return holders
            .Where(holder => contacts.ContainsKey(holder.Value))
            .ToDictionary(holder => holder.Key, holder => contacts[holder.Value]);
    }

    /// <summary>Reads the portrait of the colleague holding each address, leaving out the reader and anybody with no portrait.</summary>
    /// <remarks>
    /// The whole directory is composed to answer a handful of addresses, which is what every other read of colleagues does
    /// and is bounded by <see cref="IMemberDirectory.MaximumMemberCount" />. An organization large enough for that to
    /// show on a page read earns a lookup by address in the database, which the same ceiling's remarks already name.
    /// </remarks>
    private async Task<IEnumerable<KeyValuePair<EmailAddress, PersonPhoto>>> MemberPortraitsAsync(
        Guid organization,
        UserId reader,
        EmailAddress[] addresses,
        CancellationToken cancellationToken)
    {
        var asked = addresses.ToHashSet();
        var colleagues = await this.members.ReadAsync(organization, cancellationToken);

        return colleagues
            .Where(colleague => colleague.Member != reader && colleague.Portrait is not null)
            .SelectMany(colleague => colleague.Addresses
                .Where(asked.Contains)
                .Select(address => KeyValuePair.Create(
                    address,
                    new PersonPhoto(PersonPhotoSource.Member, colleague.Member.Value, colleague.Portrait!.Value.Value))))
            .DistinctBy(entry => entry.Key);
    }
}
