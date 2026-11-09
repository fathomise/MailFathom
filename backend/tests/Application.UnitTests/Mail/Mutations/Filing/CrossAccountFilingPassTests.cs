// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Coordination;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Mail.Mutations.Filing;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Filing;
using MailFathom.Domain.Notifications;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Filing;

public sealed class CrossAccountFilingPassTests
{
    private static readonly MailAccountId Destination = MailAccountId.Create("personal");
    private static readonly MailAccountId Source = MailAccountId.Create("work");
    private static readonly StoredEmailId Copy = StoredEmailId.Create(Guid.Parse("0196f3a0-0000-7000-8000-00000000c0c0"));
    private static readonly StoredEmailId Filed = StoredEmailId.Create(Guid.Parse("0196f3a0-0000-7000-8000-00000000f11e"));
    private static readonly MailboxMutationRecordId Removal = MailboxMutationRecordId.Create(Guid.Parse("0196f3a0-0000-7000-8000-0000000000de"));
    private static readonly MailFolderResolution Clients =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("clients"), RemoteFolderPath.Create("Clients"));
    private static readonly RestoredEmailState Read =
        new(IsSeen: true, IsAnswered: false, IsFlagged: true, IsDraft: false, EmailKeywords.Create(["$label1"]));
    private static readonly DateTimeOffset Arrived = new(2026, 10, 30, 8, 15, 0, TimeSpan.Zero);
    private static readonly byte[] RawMime = "Subject: hello\r\n\r\nbody\r\n"u8.ToArray();

    private readonly InMemoryCrossAccountFilingStore filings = new();
    private readonly InMemoryLocalEmailStateStore heldSource = new(Source);
    private readonly InMemoryNotificationStore notifications = new();
    private readonly IMailboxWriteSession writeSession = Substitute.For<IMailboxWriteSession>();
    private readonly IMailboxWriteSessionFactory writeSessions = Substitute.For<IMailboxWriteSessionFactory>();

    public CrossAccountFilingPassTests()
    {
        this.writeSessions
            .OpenForWritingAsync(Destination, Arg.Any<MailFolderResolution>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .Returns(this.writeSession);
        this.writeSession
            .AppendRestoredAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<RestoredEmailState>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(RemoteEmailPlacement.Reported(ImapUidValidity.Create(7), ImapUid.Create(41)));
    }

    [Fact]
    public async Task CarryAsync_AFilingFromAMirroredSource_AppendsWhatTheCopyCarriesAndBindsItWhereTheServerPutIt()
    {
        // Arrange
        await this.FileAsync(Removal, erasesHeldSource: false);

        // Act
        var failed = await this.Pass().CarryAsync(Destination, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, failed);
        await this.writeSession.Received(1).AppendRestoredAsync(
            Arg.Is<ReadOnlyMemory<byte>>(raw => raw.ToArray().SequenceEqual(RawMime)),
            Read,
            Arrived,
            Arg.Any<CancellationToken>());
        Assert.Equal(CrossAccountFilingStage.Appended, this.filings.Single().Stage);
        Assert.Equal((ImapUidValidity.Create(7), ImapUid.Create(41)), this.filings.BoundCopies[Copy]);
        Assert.False(this.filings.HoldsBack(Removal));
    }

    [Fact]
    public async Task CarryAsync_AnAppendTheServerTookWithoutNamingWhere_EndsTheFilingAndLetsTheDiscoveredMessageStandForTheCopy()
    {
        // Arrange
        await this.FileAsync(Removal, erasesHeldSource: false);
        this.writeSession
            .AppendRestoredAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<RestoredEmailState>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(RemoteEmailPlacement.NotReported());

        // Act
        var failed = await this.Pass().CarryAsync(Destination, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, failed);
        Assert.Equal(CrossAccountFilingStage.Appended, this.filings.Single().Stage);
        Assert.Equal([Copy], this.filings.ErasedCopies);
        Assert.Empty(this.filings.ShownSources);
        Assert.False(this.filings.HoldsBack(Removal));
        Assert.Empty(this.notifications.Recorded);
    }

    [Fact]
    public async Task CarryAsync_AFilingFromAHeldSource_ErasesTheSourceOnceTheAppendLanded()
    {
        // Arrange
        this.heldSource.Store(Filed, HeldState());
        await this.FileAsync(removal: null, erasesHeldSource: true);

        // Act
        await this.Pass().CarryAsync(Destination, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Filed], this.heldSource.Erased);
    }

    [Fact]
    public async Task CarryAsync_AServerThatCannotBeReached_HoldsBothHalvesForTheNextPass()
    {
        // Arrange
        await this.FileAsync(Removal, erasesHeldSource: false);
        this.writeSessions
            .OpenForWritingAsync(Destination, Arg.Any<MailFolderResolution>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MailboxUnavailableException(Destination, new IOException("unreachable")));

        // Act
        var failed = await this.Pass().CarryAsync(Destination, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, failed);
        Assert.Equal(CrossAccountFilingStage.Recorded, this.filings.Single().Stage);
        Assert.True(this.filings.HoldsBack(Removal));
        Assert.Empty(this.notifications.Recorded);
    }

    [Fact]
    public async Task CarryAsync_AnAppendTheServerRefused_RevertsTheWholeActAndTellsThePerson()
    {
        // Arrange
        await this.FileAsync(Removal, erasesHeldSource: false);
        this.writeSession
            .AppendRestoredAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<RestoredEmailState>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("NO [OVERQUOTA]"));

        // Act
        await this.Pass().CarryAsync(Destination, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CrossAccountFilingStage.Abandoned, this.filings.Single().Stage);
        Assert.Equal([Copy], this.filings.ErasedCopies);
        Assert.Equal([Filed], this.filings.ShownSources);
        Assert.Equal([Removal], this.filings.WithdrawnRemovals);
        var told = Assert.Single(this.notifications.Recorded);
        Assert.Equal(NotificationStatement.MailboxChangeAbandoned(MailboxChangeAct.Move), told.Statement);
        Assert.Equal(NotificationTarget.ToMessage(Filed), told.Target);
    }

    [Fact]
    public async Task CarryAsync_AnAppendWhoseAnswerNeverCame_IsNeverIssuedAgain()
    {
        // Arrange
        await this.FileAsync(Removal, erasesHeldSource: false);
        await this.filings.RecordAppendIssuedAsync(
            Substitute.For<IPersistenceSession>(),
            this.filings.Single().Id,
            TestContext.Current.CancellationToken);

        // Act
        await this.Pass().CarryAsync(Destination, TestContext.Current.CancellationToken);

        // Assert
        await this.writeSession.DidNotReceiveWithAnyArgs().AppendRestoredAsync(default, default!, default, TestContext.Current.CancellationToken);
        Assert.Equal(CrossAccountFilingStage.Abandoned, this.filings.Single().Stage);
    }

    [Fact]
    public async Task CarryAsync_ADestinationHeldSinceTheFiling_IssuesNothingToItsSource()
    {
        // Arrange
        await this.FileAsync(Removal, erasesHeldSource: false);

        // Act
        var failed = await this.Pass(InMemoryMailAccountCustodyStore.Holding(Destination))
            .CarryAsync(Destination, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, failed);
        await this.writeSessions.DidNotReceiveWithAnyArgs().OpenForWritingAsync(default, default!, default!, TestContext.Current.CancellationToken);
        Assert.Equal(CrossAccountFilingStage.Recorded, this.filings.Single().Stage);
    }

    private static LocalEmailState HeldState() => new(
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("inbox"), RemoteFolderPath.Create("INBOX")),
        HoldsSourceOccurrence: false,
        Folder: null,
        IsSeen: true,
        IsFlagged: false,
        EmailKeywords.Create([]));

    private async Task FileAsync(MailboxMutationRecordId? removal, bool erasesHeldSource)
    {
        await this.filings.RecordAsync(
            Substitute.For<IPersistenceSession>(),
            new NewCrossAccountFiling(Destination, Copy, Clients.RemotePath, Source, Filed, removal, erasesHeldSource),
            TestContext.Current.CancellationToken);
        this.filings.Store(Copy, Clients, Read, Arrived);
    }

    private CrossAccountFilingPass Pass(IMailAccountCustodyStore? custody = null)
    {
        var persistenceSession = Substitute.For<IPersistenceSession>();
        persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var persistenceSessions = Substitute.For<IPersistenceSessionFactory>();
        persistenceSessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

        var contents = Substitute.For<IEmailContentStore>();
        contents
            .FindStoredContentAsync(Copy, Arg.Any<CancellationToken>())
            .Returns(new StoredEmailContent(RawMime, RawMime.Length, new byte[32]));

        var composition = new MailboxCustodianComposition(new FakeTimeProvider(Arrived))
        {
            Custody = custody ?? InMemoryMailAccountCustodyStore.Mirroring(Destination),
            Leases = MailboxCustodianComposition.LeasesHeldBy(WorkLeaseHolder.ForBuild("0.9.0")),
            Capabilities = MailboxCustodianComposition.CapabilitiesObserved(
                MailboxServerCapabilityObservation.Observed(MailboxServerCapabilities.UidPlus, Arrived)),
            PersistenceSessions = persistenceSessions,
            Notifications = this.notifications,
            Assignments = new StubMailAccountAssignments().Assigning(SyntheticUser.Deployment, Source),
            CrossAccountFilings = this.filings,
        };

        return new CrossAccountFilingPass(
            this.filings,
            this.writeSessions,
            contents,
            Substitute.For<IMailTransportSecurityPolicyReader>(),
            this.heldSource,
            composition.CommitPolicy,
            composition.Failures,
            new MailboxConvergenceOptions(),
            composition.Custodians);
    }
}
