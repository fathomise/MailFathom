// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Persistence;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the three routes a user reads, replaces, and removes the photo of a person in their own book over. What has
/// to hold is that they answer as the routes over the user's own portrait do — a photo served private, revalidated, and
/// never sniffed, no photo answered plainly, an upload judged by its octets — and that a contact the user's own book
/// does not hold is answered as one.
/// </summary>
public sealed class ClientContactPhotoEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x0D, 0x0A];

    private static readonly StoredFileId Linked = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000001"));

    private static readonly StoredFileId Written = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000002"));

    private readonly IContactStore store = Substitute.For<IContactStore>();
    private readonly IContactDirectory directory = Substitute.For<IContactDirectory>();
    private readonly IStoredFileStore files = Substitute.For<IStoredFileStore>();

    [Fact]
    public async Task ReadAsync_AContactWithAPhoto_ServesItPrivateRevalidatedAndUnsniffed()
    {
        // Arrange
        var contact = this.Holding(Linked);
        this.files.ReadAsync(SyntheticUser.Deployment, Linked, Arg.Any<CancellationToken>()).Returns(new ReadOnlyMemory<byte>(Png));
        var context = new DefaultHttpContext();

        // Act
        var result = await ClientContactPhotoEndpoint.ReadAsync(
            contact.Id.Value,
            this.Photos(),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.IsType<FileContentHttpResult>(result.Result);
        var caching = context.Response.GetTypedHeaders().CacheControl!;

        Assert.Equal("image/png", served.ContentType);
        Assert.Equal(Png, served.FileContents.ToArray());
        Assert.NotNull(served.EntityTag);
        Assert.True(caching.Private);
        Assert.True(caching.NoCache);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
    }

    /// <summary>A client draws the initials it already has, so a contact with no photo is a plain answer rather than a refusal.</summary>
    [Fact]
    public async Task ReadAsync_AContactWithNoPhoto_AnswersThatThereIsNone()
    {
        // Arrange
        var contact = this.Holding(photo: null);

        // Act
        var result = await ClientContactPhotoEndpoint.ReadAsync(
            contact.Id.Value,
            this.Photos(),
            new DefaultHttpContext(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
    }

    [Fact]
    public async Task ReplaceAsync_OctetsThatAreNeitherKind_AreRefusedNamingTheKindsAndStoreNothing()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream("<svg onload=alert(1)>"u8.ToArray());

        // Act
        var result = await ClientContactPhotoEndpoint.ReplaceAsync(
            Guid.CreateVersion7(Now),
            this.Photos(),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, refusal.StatusCode);
        Assert.Contains("image/png", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
        await this.files.DidNotReceive().WriteAsync(
            Arg.Any<UserId>(),
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Only a person the user wrote down in their own book takes a photo, so any other is answered as a contact the book does not hold.</summary>
    [Fact]
    public async Task ReplaceAsync_AContactTheUsersOwnBookDoesNotHold_IsAnsweredNotFoundAndKeepsNoFile()
    {
        // Arrange
        this.files.WriteAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(Written);
        this.store.RelinkPhotoAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<UserId>(),
                Arg.Any<ContactId>(),
                Arg.Any<StoredFileId?>(),
                Arg.Any<CancellationToken>())
            .Returns(ContactPhotoRelinking.NoSuchContact);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Png);

        // Act
        var result = await ClientContactPhotoEndpoint.ReplaceAsync(
            Guid.CreateVersion7(Now),
            this.Photos(),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
        await this.files.Received(1).RemoveAsync(SyntheticUser.Deployment, Written, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_AContactWithAPhoto_AnswersNoContentAndRemovesTheFile()
    {
        // Arrange
        this.store.RelinkPhotoAsync(
                Arg.Any<IPersistenceSession>(),
                SyntheticUser.Deployment,
                Arg.Any<ContactId>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(new ContactPhotoRelinking(ContactHeld: true, Linked));

        // Act
        var result = await ClientContactPhotoEndpoint.RemoveAsync(
            Guid.CreateVersion7(Now),
            this.Photos(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
        await this.files.Received(1).RemoveAsync(SyntheticUser.Deployment, Linked, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_TheEmptyIdentifier_IsRefusedAsARequestRatherThanAContact()
    {
        // Act
        var result = await ClientContactPhotoEndpoint.RemoveAsync(
            Guid.Empty,
            this.Photos(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    private ContactPhotos Photos()
    {
        var authorization = AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.ContactsRead,
            MailFathomPermission.ContactsWrite);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new ContactPhotos(
            authorization,
            ContactBookOwnerships.For(authorization),
            this.directory,
            this.store,
            this.files,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), new FakeTimeProvider(Now)),
            ClientSignalPublishers.ReachingNobody);
    }

    private Contact Holding(StoredFileId? photo)
    {
        var address = EmailAddress.TryCreate(displayName: null, "jan@example.test", out var parsed)
            ? parsed
            : throw new InvalidOperationException("The test address names no mailbox.");
        var contact = Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create("Jan Nowak"),
            [address],
            address,
            note: null,
            ContactOrigin.Asserted,
            Now,
            Now,
            photo: photo?.Value);

        this.directory.FindAsync(Arg.Any<ContactBookScope>(), contact.Id, Arg.Any<CancellationToken>()).Returns(contact);

        return contact;
    }

    /// <summary>A session that commits whatever was staged in it, which is what a write's ordinary path needs.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
