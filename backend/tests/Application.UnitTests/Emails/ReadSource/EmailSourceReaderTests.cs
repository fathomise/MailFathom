// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ReadSource;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ReadSource;

/// <summary>Covers what a reader is served as a message's source, and every way there is none to serve.</summary>
public sealed class EmailSourceReaderTests
{
    private static readonly byte[] StoredRawMime =
        Encoding.UTF8.GetBytes("From: sender@example.test\r\nSubject: Quarterly figures\r\n\r\nBody");

    private static readonly AuthorizedPrincipal SignedInReader =
        AuthorizedPrincipal.CallerActingFor(SyntheticUser.Deployment, "client-key", [MailFathomPermission.MailRead]);

    [Fact]
    public async Task ReadForReaderAsync_IntactLocalCopy_ServesEveryOctetAsStored()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(summary);

        // Act
        var source = await reader.ReadForReaderAsync(summary.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredRawMime, source.Octets?.ToArray());
        Assert.False(source.IsCut);
        Assert.Equal(StoredRawMime.Length, source.WholeOctetCount);
    }

    /// <summary>
    /// A message larger than the bound is served up to it and no further, and the size of the whole travels beside the
    /// part so the reader can be told where it stops.
    /// </summary>
    [Fact]
    public async Task ReadForReaderAsync_SourceLargerThanTheBound_ServesTheBoundAndTheWholeSize()
    {
        // Arrange
        var largeMime = new byte[EmailSourceReader.MaximumServedOctets + 4096];
        Array.Fill(largeMime, (byte)'A');
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            contentStore: ContentStoreReturning(new StoredEmailContent(largeMime, largeMime.Length, SHA256.HashData(largeMime))));

        // Act
        var source = await reader.ReadForReaderAsync(summary.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSourceReader.MaximumServedOctets, source.Octets?.Length);
        Assert.True(source.IsCut);
        Assert.Equal(largeMime.Length, source.WholeOctetCount);
    }

    [Fact]
    public async Task ReadForReaderAsync_EmailThisDeploymentDoesNotHold_ServesNothingAndReadsNoContent()
    {
        // Arrange
        var contentStore = ContentStores.Substituted();
        var reader = ReaderOver(summary: null, contentStore: contentStore);

        // Act
        var source = await reader.ReadForReaderAsync(
            StoredEmailId.Create(Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(source.Octets);
        Assert.False(source.Screened);
        await contentStore.DidNotReceive().FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Another user's message is answered exactly as one that does not exist, so a reader learns nothing about mail they
    /// cannot read, and none of its content is read.
    /// </summary>
    [Fact]
    public async Task ReadForReaderAsync_EmailOfAnAccountThisCallerDoesNotReach_ServesNothingAndReadsNoContent()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var contentStore = ContentStores.Substituted();
        var reader = ReaderOver(
            summary,
            contentStore: contentStore,
            accountCatalog: CatalogServing(MailAccountId.Create("somebody-else")));

        // Act
        var source = await reader.ReadForReaderAsync(summary.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(source.Octets);
        await contentStore.DidNotReceive().FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A message whose copy was deliberately not stored has no source here, and nothing is queued to repair it.</summary>
    [Fact]
    public async Task ReadForReaderAsync_EmailWhoseCopyWasNeverStored_ServesNothingAndRecordsNoRepair()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create() with
        {
            ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit,
        };
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(summary, repairRequestStore: repairRequests);

        // Act
        var source = await reader.ReadForReaderAsync(summary.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(source.Octets);
        Assert.Empty(repairRequests.Recorded);
    }

    /// <summary>A defect a source read finds is the defect a body read would have found, so it is recorded the same way.</summary>
    [Theory]
    [InlineData(nameof(EmailContentDefect.Missing))]
    [InlineData(nameof(EmailContentDefect.ByteLengthMismatch))]
    public async Task ReadForReaderAsync_DamagedOrMissingLocalCopy_ServesNothingAndRecordsARepairRequest(string defect)
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summary,
            contentStore: ContentStoreReturning(StoredContentFor(defect)),
            repairRequestStore: repairRequests);

        // Act
        var source = await reader.ReadForReaderAsync(summary.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(source.Octets);
        var recorded = Assert.Single(repairRequests.Recorded);
        Assert.Equal(summary.StoredEmailId, recorded.StoredEmailId);
        Assert.Equal(defect, recorded.Defect.ToString());
    }

    [Fact]
    public async Task ReadForReaderAsync_ContentServedFromTheRetainedCopy_ServesItAndRecordsARepairRequest()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summary,
            contentStore: ContentStoreReturning(IntactContent() with { WasServedFromRetainedCopy = true }),
            repairRequestStore: repairRequests);

        // Act
        var source = await reader.ReadForReaderAsync(summary.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(source.Octets);
        Assert.Equal(EmailContentDefect.ObjectUnreadable, Assert.Single(repairRequests.Recorded).Defect);
    }

    /// <summary>
    /// Raw octets cannot be redacted, so an account whose mail is screened is refused its source before any of it is
    /// read, and the refusal is distinct from there being none.
    /// </summary>
    [Fact]
    public async Task ReadForReaderAsync_AccountWhoseMailIsScreened_RefusesAsScreenedWithoutReadingContent()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var contentStore = ContentStoreReturning(IntactContent());
        using var egress = ScanningSensitiveContentEgress.Finding("sk-live-000111222333", TimeProvider.System);
        var reader = ReaderOver(summary, contentStore: contentStore, egressGuard: egress.Guard);

        // Act
        var source = await reader.ReadForReaderAsync(summary.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(source.Screened);
        Assert.Null(source.Octets);
        await contentStore.DidNotReceive().FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadForReaderAsync_CallerGrantedNothing_RefusesWithoutReadingContent()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var contentStore = ContentStoreReturning(IntactContent());
        var reader = ReaderOver(
            summary,
            contentStore: contentStore,
            authorization: AuthorizationOver(AuthorizedPrincipal.Caller("client-key", [])));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(async () =>
            await reader.ReadForReaderAsync(summary.StoredEmailId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
        await contentStore.DidNotReceive().FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Reading a source must never fetch a message or set <c>\Seen</c>. The use case holds no mailbox port, so the
    /// guarantee is structural rather than a rule somebody has to keep.
    /// </summary>
    [Fact]
    public void EmailSourceReader_ItsDependencies_IncludeNoMailboxPort()
    {
        // Arrange
        Type[] mailboxPorts =
        [
            typeof(IMailboxSessionFactory),
            typeof(IMailboxSession),
            typeof(IMailboxNotificationSessionFactory),
        ];

        // Act
        var dependencies = typeof(EmailSourceReader)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        // Assert
        Assert.Empty(dependencies.Intersect(mailboxPorts));
    }

    private static EmailSourceReader ReaderOver(
        EmailSummary? summary,
        IEmailContentStore? contentStore = null,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        SensitiveContentEgressGuard? egressGuard = null,
        ICallerMailAccountCatalog? accountCatalog = null,
        AccessAuthorization? authorization = null)
    {
        var participation = summary is null
            ? StubMailFolderParticipation.Nothing
            : StubMailFolderParticipation.Mapping(new MailFolderIdentity(summary.Account, summary.Folder.Alias!.Value));

        return new EmailSourceReader(
            SummaryReaderReturning(summary),
            contentStore ?? ContentStoreReturning(IntactContent()),
            repairRequestStore ?? new RecordingEmailContentRepairRequestStore(),
            new MailboxScopeResolver(
                accountCatalog ?? CatalogServing(summary?.Account ?? MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId)),
                participation,
                StubDeploymentMailFolders.Of(participation),
                StubMailFolderMappings.ResolvingNothing),
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            authorization ?? AuthorizationOver(SignedInReader));
    }

    private static AccessAuthorization AuthorizationOver(AuthorizedPrincipal principal)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(principal);

        return new AccessAuthorization(principals);
    }

    private static IStoredEmailSummaryReader SummaryReaderReturning(EmailSummary? summary)
    {
        var reader = Substitute.For<IStoredEmailSummaryReader>();
        reader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(summary));

        return reader;
    }

    private static IEmailContentStore ContentStoreReturning(StoredEmailContent? storedContent)
    {
        var contentStore = ContentStores.Substituted();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(storedContent));

        return contentStore;
    }

    private static ICallerMailAccountCatalog CatalogServing(MailAccountId servedAccountId)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns([SyntheticServedAccount.Of(servedAccountId)]);

        return catalog;
    }

    private static StoredEmailContent IntactContent() =>
        new(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime));

    private static StoredEmailContent? StoredContentFor(string defect) => defect switch
    {
        nameof(EmailContentDefect.Missing) => null,
        _ => new StoredEmailContent(StoredRawMime, StoredRawMime.Length + 1, SHA256.HashData(StoredRawMime)),
    };
}
