// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Application.Persistence;
using MailFathom.Application.Portraits;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts.Photos;

/// <summary>Reads, replaces, and removes the photo of a person the signed-in user wrote down in their own book.</summary>
/// <remarks>
/// <para>
/// A contact's photo is stored exactly as the user's own portrait is: a file of the user's in
/// <see cref="IStoredFileStore" />, its kind proven from the octets by <see cref="Portrait" />, linked from the record
/// rather than held in it. Only a contact of the user's own book carries one, because a stored file belongs to a user
/// and a collected contact or an organization's contact has no user to own it.
/// </para>
/// <para>
/// A replacement writes the new file, then links it, then removes the file it displaced, so a failure between any two
/// steps leaves at most a file nothing links to, which <see cref="UnlinkedStoredFileSweep" /> removes — never a contact
/// linking to a file that is gone.
/// </para>
/// <para>
/// Reading is admitted under <see cref="MailFathomPermission.ContactsRead" /> and the two writes under
/// <see cref="MailFathomPermission.ContactsWrite" />, the grants every other act on the book asks for. Nothing is fetched
/// from anywhere: a photo is what the user supplied, and nothing here looks one up for an address.
/// </para>
/// </remarks>
public sealed class ContactPhotos
{
    private readonly AccessAuthorization authorization;
    private readonly ContactBookOwnership ownership;
    private readonly IContactDirectory directory;
    private readonly IContactStore store;
    private readonly IStoredFileStore files;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds.</param>
    /// <param name="ownership">Answers whose book the caller writes and which books they read.</param>
    /// <param name="directory">Reads the contact whose photo is asked for.</param>
    /// <param name="store">Rewrites the link from the contact to its photo.</param>
    /// <param name="files">Holds the file the photo is.</param>
    /// <param name="commitPolicy">Commits each relinking, retrying a lost race from a fresh read.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public ContactPhotos(
        AccessAuthorization authorization,
        ContactBookOwnership ownership,
        IContactDirectory directory,
        IContactStore store,
        IStoredFileStore files,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(commitPolicy);

        this.authorization = authorization;
        this.ownership = ownership;
        this.directory = directory;
        this.store = store;
        this.files = files;
        this.commitPolicy = commitPolicy;
    }

    /// <summary>Reads the photo of one contact the caller sees.</summary>
    /// <param name="contactId">The contact.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The photo, or <see langword="null" /> where the caller sees no such contact or it has none.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ContactsRead" />.</exception>
    /// <remarks>Octets that are no image kind this build stores are answered as no photo, because a file nothing here could have written is a file this surface has nothing to say about.</remarks>
    public async Task<Portrait?> ReadAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ContactsRead);

        var scope = await this.ownership.ReadScopeAsync(cancellationToken);

        if (await this.directory.FindAsync(scope, contactId, cancellationToken) is not { Photo: { } photo })
        {
            return null;
        }

        return await this.files.ReadAsync(this.ownership.User, StoredFileId.Create(photo), cancellationToken) is { } content
            ? Portrait.Of(content)
            : null;
    }

    /// <summary>Replaces the photo of one contact of the caller's own book.</summary>
    /// <param name="contactId">The contact.</param>
    /// <param name="photo">The photo, whose octets are stored as they were supplied.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the caller's own book holds the contact and the photo landed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="photo" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ContactsWrite" />.</exception>
    /// <exception cref="ContactReadOnlyException">Thrown when the contact is one the caller's organization keeps, before anything is stored.</exception>
    /// <remarks>A photo for a contact the book does not hold leaves the file written for it removed again, so a refusal stores nothing.</remarks>
    public async Task<bool> ReplaceAsync(ContactId contactId, Portrait photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);

        this.authorization.RequirePermission(MailFathomPermission.ContactsWrite);

        await this.RefuseOrganizationContactAsync(contactId, cancellationToken);

        var user = this.ownership.User;

        if (await this.files.WriteAsync(user, photo.Type.MediaType, photo.Content, cancellationToken) is not { } written)
        {
            return false;
        }

        var relinked = await this.RelinkAsync(user, contactId, written, cancellationToken);

        if (!relinked.ContactHeld)
        {
            await this.files.RemoveAsync(user, written, cancellationToken);

            return false;
        }

        if (relinked.Replaced is { } displaced && displaced != written)
        {
            await this.files.RemoveAsync(user, displaced, cancellationToken);
        }

        return true;
    }

    /// <summary>Removes the photo of one contact of the caller's own book.</summary>
    /// <param name="contactId">The contact.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the caller's own book holds the contact, whether or not it had a photo.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ContactsWrite" />.</exception>
    /// <exception cref="ContactReadOnlyException">Thrown when the contact is one the caller's organization keeps.</exception>
    /// <remarks>The link goes before the file does, for the reason a replacement orders its steps.</remarks>
    public async Task<bool> RemoveAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ContactsWrite);

        await this.RefuseOrganizationContactAsync(contactId, cancellationToken);

        var user = this.ownership.User;
        var relinked = await this.RelinkAsync(user, contactId, photo: null, cancellationToken);

        if (relinked.Replaced is { } displaced)
        {
            await this.files.RemoveAsync(user, displaced, cancellationToken);
        }

        return relinked.ContactHeld;
    }

    /// <summary>Refuses a write to the photo of a contact the caller's organization keeps, rather than answering it as one the caller does not hold.</summary>
    private async Task RefuseOrganizationContactAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        var scope = await this.ownership.ReadScopeAsync(cancellationToken);

        if (await OrganizationContacts.FindAsync(this.directory, scope, contactId, cancellationToken) is not null)
        {
            throw new ContactReadOnlyException();
        }
    }

    private Task<ContactPhotoRelinking> RelinkAsync(
        UserId user,
        ContactId contactId,
        StoredFileId? photo,
        CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            (session, token) => this.store.RelinkPhotoAsync(session, user, contactId, photo, token),
            cancellationToken);
}
