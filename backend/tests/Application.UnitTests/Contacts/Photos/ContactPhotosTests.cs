// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Persistence;
using MailFathom.Application.Portraits;
using MailFathom.Application.Signals;
using MailFathom.Application.StoredFiles;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts.Photos;

/// <summary>
/// Covers the use case a user reads, replaces, and removes the photo of a person in their own book through. What it has
/// to hold is that only a contact the user wrote down in their own book takes one, that a replacement writes before it
/// links and removes the file it displaced, that a refused photo leaves nothing stored, and that each act asks for the
/// grant the rest of the book asks for.
/// </summary>
public sealed class ContactPhotosTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserId User = SyntheticUser.Deployment;

    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x0D, 0x0A];

    private static readonly StoredFileId Linked = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000001"));

    private static readonly StoredFileId Written = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000002"));

    [Fact]
    public async Task ReadAsync_AContactLinkingAPhoto_AnswersItUnderTheKindItIs()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = OwnContact(Linked);
        book.Hold(User, contact);
        var files = Substitute.For<IStoredFileStore>();
        files.ReadAsync(User, Linked, Arg.Any<CancellationToken>()).Returns(new ReadOnlyMemory<byte>(Png));

        // Act
        var read = await PhotosOver(book, files).ReadAsync(contact.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("image/png", read!.Type.MediaType);
        Assert.Equal(Png, read.Content.ToArray());
    }

    /// <summary>A client draws the initials it already has, so a contact with no photo is a state of the screen rather than an error.</summary>
    [Fact]
    public async Task ReadAsync_AContactLinkingNoPhoto_AnswersNothingWithoutReadingAFile()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = OwnContact(photo: null);
        book.Hold(User, contact);
        var files = Substitute.For<IStoredFileStore>();

        // Act
        var read = await PhotosOver(book, files).ReadAsync(contact.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(read);
        await files.DidNotReceive().ReadAsync(Arg.Any<UserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceAsync_AContactWithAPhoto_LinksTheNewFileAndRemovesTheOneItDisplaced()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = OwnContact(Linked);
        book.Hold(User, contact);
        var files = FilesWriting(Written);

        // Act
        var landed = await PhotosOver(book, files).ReplaceAsync(contact.Id, Portrait.Of(Png)!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(landed);
        Assert.Equal(Written.Value, book.ContactsOf(User).Single().Photo);
        await files.Received(1).WriteAsync(User, "image/png", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        await files.Received(1).RemoveAsync(User, Linked, Arg.Any<CancellationToken>());
        await files.DidNotReceive().RemoveAsync(Arg.Any<UserId>(), Written, Arg.Any<CancellationToken>());
    }

    /// <summary>A link that failed leaves the new file unlinked for the sweep, and must never have taken the displaced one with it.</summary>
    [Fact]
    public async Task ReplaceAsync_ALinkThatFails_LeavesThePhotoInForceWhereItWas()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = OwnContact(Linked);
        book.Hold(User, contact);
        var store = Substitute.For<IContactStore>();
        store.RelinkPhotoAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<UserId>(),
                Arg.Any<ContactId>(),
                Arg.Any<StoredFileId?>(),
                Arg.Any<CancellationToken>())
            .Returns<ContactPhotoRelinking>(_ => throw new InvalidOperationException("The record refused the link."));
        var files = FilesWriting(Written);

        // Act
        var refusal = await Record.ExceptionAsync(
            () => PhotosOver(book, store, files).ReplaceAsync(contact.Id, Portrait.Of(Png)!, TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(refusal);
        await files.DidNotReceive().RemoveAsync(Arg.Any<UserId>(), Linked, Arg.Any<CancellationToken>());
    }

    /// <summary>A collected contact has no user to own a file, so a photo for one is refused and the file written for it removed again.</summary>
    [Fact]
    public async Task ReplaceAsync_AContactOutsideTheUsersOwnBook_IsRefusedAndStoresNothing()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var collected = Contact.Create(
            AContactId(),
            ContactDisplayName.Create("Anna Kowalska"),
            [Address("anna@example.test")],
            Address("anna@example.test"),
            note: null,
            ContactOrigin.Collected,
            Now,
            Now);
        book.Hold(SyntheticMailAccount.Deployment, collected);
        var files = FilesWriting(Written);

        // Act
        var landed = await PhotosOver(book, files).ReplaceAsync(collected.Id, Portrait.Of(Png)!, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(landed);
        Assert.Null(book.Contacts.Single().Photo);
        await files.Received(1).RemoveAsync(User, Written, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_AContactWithAPhoto_UnlinksItAndRemovesTheFile()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = OwnContact(Linked);
        book.Hold(User, contact);
        var files = Substitute.For<IStoredFileStore>();

        // Act
        var held = await PhotosOver(book, files).RemoveAsync(contact.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(held);
        Assert.Null(book.ContactsOf(User).Single().Photo);
        await files.Received(1).RemoveAsync(User, Linked, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_AContactNobodyHolds_AnswersSoAndRemovesNothing()
    {
        // Arrange
        var files = Substitute.For<IStoredFileStore>();

        // Act
        var held = await PhotosOver(new InMemoryContactBookStore(), files).RemoveAsync(AContactId(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(held);
        await files.DidNotReceive().RemoveAsync(Arg.Any<UserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceAsync_ACallerGrantedOnlyReading_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var files = Substitute.For<IStoredFileStore>();
        var photos = PhotosOver(new InMemoryContactBookStore(), files, MailFathomPermission.ContactsRead);

        // Act
        var refusal = await Record.ExceptionAsync(
            () => photos.ReplaceAsync(AContactId(), Portrait.Of(Png)!, TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
        await files.DidNotReceive().WriteAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_ACallerGrantedOnlyReading_IsRefusedBeforeAnythingIsRemoved()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = OwnContact(Linked);
        book.Hold(User, contact);
        var files = Substitute.For<IStoredFileStore>();
        var photos = PhotosOver(book, files, MailFathomPermission.ContactsRead);

        // Act
        var refusal = await Record.ExceptionAsync(
            () => photos.RemoveAsync(contact.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
        Assert.Equal(Linked.Value, book.ContactsOf(User).Single().Photo);
        await files.DidNotReceive().RemoveAsync(Arg.Any<UserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_ACallerGrantedOnlyWriting_IsRefused()
    {
        // Arrange
        var photos = PhotosOver(new InMemoryContactBookStore(), Substitute.For<IStoredFileStore>(), MailFathomPermission.ContactsWrite);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => photos.ReadAsync(AContactId(), TestContext.Current.CancellationToken));
    }

    /// <summary>A photo relinked on a contact the user holds tells their clients the book moved; one naming nobody tells nobody.</summary>
    [Fact]
    public async Task RemoveAsync_AContactTheUserHoldsAndOneNobodyHolds_AnnouncesOnlyTheFirst()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = OwnContact(Linked);
        book.Hold(User, contact);
        var clock = new FakeTimeProvider(Now);
        var channel = new RecordingClientSignalChannel();
        await using var signals = new ClientSignals([channel], clock);
        var photos = PhotosOver(book, book, Substitute.For<IStoredFileStore>(), signals);

        // Act
        await photos.RemoveAsync(AContactId(), TestContext.Current.CancellationToken);
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();
        await photos.RemoveAsync(contact.Id, TestContext.Current.CancellationToken);
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        Assert.Equal(
            ClientSignal.ContactsChanged(ContactBookHolder.Of(User)).Scope,
            Assert.Single(channel.Published).Scope);
    }

    private static ContactPhotos PhotosOver(
        InMemoryContactBookStore book,
        IStoredFileStore files,
        params MailFathomPermission[] permissions) =>
        PhotosOver(book, book, files, permissions);

    private static ContactPhotos PhotosOver(
        InMemoryContactBookStore book,
        IContactStore store,
        IStoredFileStore files,
        params MailFathomPermission[] permissions) =>
        PhotosOver(book, store, files, ClientSignalPublishers.ReachingNobody, permissions);

    private static ContactPhotos PhotosOver(
        InMemoryContactBookStore book,
        IContactStore store,
        IStoredFileStore files,
        ClientSignals signals,
        params MailFathomPermission[] permissions)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(
            permissions.Length == 0 ? [MailFathomPermission.ContactsRead, MailFathomPermission.ContactsWrite] : permissions);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new ContactPhotos(
            authorization,
            ContactBookOwnerships.For(authorization, SyntheticMailAccount.Deployment),
            book,
            store,
            files,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), new FakeTimeProvider(Now)),
            signals);
    }

    private static IStoredFileStore FilesWriting(StoredFileId written)
    {
        var files = Substitute.For<IStoredFileStore>();
        files.WriteAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(written);

        return files;
    }

    private static Contact OwnContact(StoredFileId? photo) => Contact.Create(
        AContactId(),
        ContactDisplayName.Create("Jan Nowak"),
        [Address("jan@example.test")],
        Address("jan@example.test"),
        note: null,
        ContactOrigin.Asserted,
        Now,
        Now,
        photo: photo?.Value);

    private static ContactId AContactId() => ContactId.Create(Guid.CreateVersion7(Now));

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress)
            ? emailAddress
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");

    /// <summary>A session that commits, because nothing here is about a conflict the policy has to retry.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
