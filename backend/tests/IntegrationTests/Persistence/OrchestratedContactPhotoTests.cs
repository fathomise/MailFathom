// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Persistence;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the photo of a person in a user's own book against a real database: that linking one names it on the record
/// and answers the file it displaced, that the sweep's candidate query leaves a file a contact links, and that erasing
/// the contact takes the file with it. Each is a statement over rows only PostgreSQL holds, which is why the stores carry
/// the integration-coverage marker.
/// </summary>
/// <remarks>
/// Every test writes against a user it provisions and erases in a <c>finally</c>, for the reason
/// <c>OrchestratedForeignUser</c> states.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedContactPhotoTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly byte[] Picture =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x04, 0x05, 0x06];

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RelinkPhotoAsync_APhotoReplaced_NamesTheNewFileAndAnswersTheOneItDisplaced()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(services, owner, cancellationToken);

        try
        {
            var contact = await RecordAsync(services, owner, cancellationToken);
            var first = (await WriteAsync(services, owner, cancellationToken))!.Value;
            var second = (await WriteAsync(services, owner, cancellationToken))!.Value;

            await RelinkAsync(services, owner, contact, first, cancellationToken);

            // Act
            var relinking = await RelinkAsync(services, owner, contact, second, cancellationToken);

            // Assert
            var held = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IContactDirectory>()
                    .FindAsync(ContactBookScope.OfOwnBookAlone(Named(owner)), contact, token),
                cancellationToken);

            Assert.True(relinking.ContactHeld);
            Assert.Equal(first, relinking.Replaced);
            Assert.Equal(second.Value, held?.Photo);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, owner);
        }
    }

    [Fact]
    public async Task FindUnmentionedAsync_AFileAContactLinks_IsNotNamedWhileTheUnlinkedOneIs()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(services, owner, cancellationToken);

        try
        {
            var contact = await RecordAsync(services, owner, cancellationToken);
            var linked = (await WriteAsync(services, owner, cancellationToken))!.Value;
            var unlinked = (await WriteAsync(services, owner, cancellationToken))!.Value;

            await RelinkAsync(services, owner, contact, linked, cancellationToken);

            // Act
            var candidates = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IStoredFileStore>()
                    .FindUnmentionedAsync(DateTimeOffset.MaxValue, 1000, token),
                cancellationToken);

            // Assert
            var owned = candidates.Where(candidate => candidate.Owner == Named(owner)).Select(candidate => candidate.File);

            Assert.Equal([unlinked], owned);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, owner);
        }
    }

    [Fact]
    public async Task EraseAsync_AContactWithAPhoto_TakesThePhotosFileWithIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(services, owner, cancellationToken);

        try
        {
            var contact = await RecordAsync(services, owner, cancellationToken);
            var photo = (await WriteAsync(services, owner, cancellationToken))!.Value;

            await RelinkAsync(services, owner, contact, photo, cancellationToken);

            // Act
            var erased = await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IContactStore>()
                    .EraseAsync(session, ContactBookScope.OfOwnBookAlone(Named(owner)), contact, token),
                cancellationToken);

            // Assert
            Assert.Equal(PersistenceCommitResult.Committed, erased);
            Assert.Equal(0, await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredFiles
                    .CountAsync(file => file.UserId == owner, token),
                cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, owner);
        }
    }

    private static UserId Named(Guid user) => UserId.Create(user);

    private static async Task<ContactId> RecordAsync(
        OrchestratedMailFathomServices services,
        Guid owner,
        CancellationToken cancellationToken)
    {
        var contactId = Guid.CreateVersion7(RecordedAt);
        var bookHolder = ContactBookHolder.Of(Named(owner)).Key;
        var address = $"photo-{contactId:N}@contacts.test";

        var committed = await services.CommitAsync(
            async (_, session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                context.Contacts.Add(new ContactEntity
                {
                    Id = contactId,
                    BookHolderId = bookHolder,
                    UserId = owner,
                    DisplayName = "Photographed Person",
                    DisplayNameSortKey = "PHOTOGRAPHED PERSON",
                    PreferredNormalizedAddress = address,
                    Origin = ContactOrigin.Asserted,
                    RecordedAt = RecordedAt,
                    AmendedAt = RecordedAt,
                });
                context.ContactAddresses.Add(new ContactAddressEntity
                {
                    Id = Guid.CreateVersion7(RecordedAt),
                    ContactId = contactId,
                    BookHolderId = bookHolder,
                    Address = address,
                    NormalizedAddress = address,
                });

                await context.SaveChangesAsync(token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);

        return ContactId.Create(contactId);
    }

    private static Task<StoredFileId?> WriteAsync(
        OrchestratedMailFathomServices services,
        Guid owner,
        CancellationToken cancellationToken) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredFileStore>().WriteAsync(Named(owner), "image/png", Picture, token),
            cancellationToken);

    private static async Task<ContactPhotoRelinking> RelinkAsync(
        OrchestratedMailFathomServices services,
        Guid owner,
        ContactId contact,
        StoredFileId photo,
        CancellationToken cancellationToken)
    {
        ContactPhotoRelinking? relinking = null;

        var committed = await services.CommitAsync(
            async (scope, session, token) => relinking = await scope.GetRequiredService<IContactStore>()
                .RelinkPhotoAsync(session, Named(owner), contact, photo, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);

        return relinking!;
    }
}
