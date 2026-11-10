// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Coordination;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Mail.Retention;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Retention;

/// <summary>Covers which mail a night retires, what it asks the account's own delete to do, and what stops it.</summary>
public sealed class MailMessageRetirementTests
{
    private const int MaximumInspectedLeases = 512;

    private static readonly MailAccountId Account = MailAccountId.Create("0199a0c0-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 2, 0, 0, TimeSpan.Zero);

    private static readonly MailFolderResolution Inbox =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("INBOX"), RemoteFolderPath.Create("INBOX"));

    private static readonly MailFolderId InboxRow = MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000e6"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>With the switch off nothing is read and nothing is asked for.</summary>
    [Fact]
    public async Task RetireAsync_TheSwitchOff_DoesNothing()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.MessageRetention.GetMessageRetention(Account).Returns(MailMessageRetention.Disabled);

        // Act
        var retired = await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        Assert.Equal(0, retired);
        await fixture.Sessions.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
        Assert.Equal(0, fixture.Records.OpenedRecordCount);
    }

    /// <summary>The delete names retention as what asked and carries what the account's own deletes do, resolved when it is recorded.</summary>
    [Fact]
    public async Task RetireAsync_AMessagePastTheAge_RecordsTheAccountsOwnDeleteNamingRetention()
    {
        // Arrange
        var fixture = new Fixture();
        var mail = Scheduled(uid: 7);
        fixture.Age([mail]);

        // Act
        var retired = await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        Assert.Equal(1, retired);
        var request = Assert.Single(fixture.Records.OpenedRequests);
        Assert.Equal(MailboxMutation.Delete, request.Mutation);
        Assert.Equal(MailboxMutationOrigin.Retention, request.Requester.Origin);
        Assert.Equal("retention@2026-11-10", request.Requester.Identity);
        Assert.Equal(AuthoredDeleteEmailDisposition.RetainTombstone, request.LocalDisposition);
        Assert.Equal(AuthoredDeleteServerDisposition.FlagDeleted, request.ServerDisposition);
        await fixture.Session.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The age and the exclusions the account states are what the store is asked for, measured from the clock.</summary>
    [Fact]
    public async Task RetireAsync_ReadingTheBatch_AsksForTheAccountsOwnAge()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Age([]);

        // Act
        await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        await fixture.AgedMail.Received(1).ReadAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Is<MailMessageRetention>(retention => retention.ExcludedFolders.Contains("Archive")),
            Now - TimeSpan.FromDays(730),
            null,
            MailMessageRetirement.MessagesRetiredPerTransaction,
            Arg.Any<CancellationToken>());
    }

    /// <summary>No run schedules this folder, so no backward pass would ever dispose of the row: the local copy goes and the server copy stays.</summary>
    [Fact]
    public async Task RetireAsync_AFolderNoRunSchedules_LeavesTheServerUntouched()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Mappings.FindFolderNamed(Account, Inbox.Alias).Returns(
            MailFolderMapping.ToRemotePath(
                Inbox.Alias,
                Inbox.RemotePath,
                MailFolderParticipation.Create(isSynchronized: false, generatesEmbeddings: false, isVisibleToAI: false)));
        fixture.Age([Scheduled(uid: 7)]);

        // Act
        await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        Assert.True(Assert.Single(fixture.Records.OpenedRequests).LeavesServerUntouched);
    }

    /// <summary>The departure for an unscheduled folder is a mirrored account's alone: a held account's drain owes the source what the account's own deletes say.</summary>
    [Fact]
    public async Task RetireAsync_AHeldMessageInAFolderNoRunSchedules_CarriesTheAccountsOwnServerDisposition()
    {
        // Arrange
        var fixture = new Fixture(folders: new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Held));
        var mail = Scheduled(uid: 7);
        fixture.States.Store(
            mail.Email,
            new LocalEmailState(Inbox, HoldsSourceOccurrence: true, InboxRow, IsSeen: false, IsFlagged: false, EmailKeywords.Create([])));
        fixture.Mappings.FindFolderNamed(Account, Inbox.Alias).Returns(
            MailFolderMapping.ToRemotePath(
                Inbox.Alias,
                Inbox.RemotePath,
                MailFolderParticipation.Create(isSynchronized: false, generatesEmbeddings: false, isVisibleToAI: false)));
        fixture.ReturnsCustody(MailAccountCustodyPhase.Held);
        fixture.Age([mail]);

        // Act
        await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        var request = Assert.Single(fixture.Records.OpenedRequests);
        Assert.Equal(AuthoredDeleteServerDisposition.FlagDeleted, request.ServerDisposition);
        Assert.False(request.LeavesServerUntouched);
    }

    /// <summary>A held account's drained message has no occurrence, so it is erased directly and an open client is told once the batch has committed.</summary>
    [Fact]
    public async Task RetireAsync_ADrainedHeldMessage_IsErasedAndAnnouncedAfterTheCommit()
    {
        // Arrange
        var channel = Substitute.For<IClientSignalChannel>();
        await using var signals = new ClientSignals([channel], new FakeTimeProvider(Now));
        var fixture = new Fixture(signals);
        var email = StoredEmailId.Create(Guid.CreateVersion7());
        fixture.States.Store(
            email,
            new LocalEmailState(Inbox, HoldsSourceOccurrence: false, InboxRow, IsSeen: false, IsFlagged: false, EmailKeywords.Create([])));
        fixture.ReturnsCustody(MailAccountCustodyPhase.Held);
        fixture.Age([new AgedStoredMail(email, InboxRow, Inbox.Alias, Occurrence: null)]);

        // Act
        var retired = await fixture.Retirement.RetireAsync(Account, Token);
        await signals.DisposeAsync();

        // Assert
        Assert.Equal(1, retired);
        Assert.Contains(email, fixture.States.Erased);
        await channel.Received(1).PublishAsync(
            Arg.Is<ClientSignal>(signal => signal != null
                && signal.Kind == ClientSignalKind.MailChanged
                && signal.Folder == InboxRow
                && signal.Emails.SequenceEqual(new[] { email })),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A rolled-back batch retires nothing, so nothing is announced and a later occasion repeats it.</summary>
    [Fact]
    public async Task RetireAsync_ACommitThatConflicted_StopsAndAnnouncesNothing()
    {
        // Arrange
        var channel = Substitute.For<IClientSignalChannel>();
        await using var signals = new ClientSignals([channel], new FakeTimeProvider(Now));
        var fixture = new Fixture(signals);
        fixture.Session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.ConcurrencyConflict);
        fixture.ReturnsCustody(MailAccountCustodyPhase.Held);
        fixture.Age([new AgedStoredMail(StoredEmailId.Create(Guid.CreateVersion7()), InboxRow, Inbox.Alias, Occurrence: null)]);

        // Act
        var retired = await fixture.Retirement.RetireAsync(Account, Token);
        await signals.DisposeAsync();

        // Assert
        Assert.Equal(0, retired);
        await channel.DidNotReceive().PublishAsync(Arg.Any<ClientSignal>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A restoring account runs no pass, and neither does an account the custody read finds in no phase at all.</summary>
    [Fact]
    public async Task RetireAsync_ARestoringAccount_ReadsNothing()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Restoring);

        // Act
        var retired = await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        Assert.Equal(0, retired);
        await fixture.AgedMail.DidNotReceiveWithAnyArgs()
            .ReadAsync(default!, default!, default!, default, default, default, Token);
    }

    /// <summary>While a replica of an older build holds a lease it could not read this requester's records, so nothing is written.</summary>
    [Fact]
    public async Task RetireAsync_AReplicaOfAnOlderBuild_WritesNothingThisNight()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Leases.ReadEveryHeldAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<WorkLease>>(
            [
                new WorkLease(
                    WorkScope.Create("mail-account:other"),
                    WorkLeaseHolder.Create("0.8.0/overlay/0199c1a2-0000-7000-8000-0000000000a1"),
                    ReplicaIdentity.Create("replica-2"),
                    Now.AddMinutes(1)),
            ]));

        // Act
        var retired = await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        Assert.Equal(0, retired);
        await fixture.Sessions.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>A page of leases that fills its bound may hide an older build's lease in the part not read, so it answers no; one short of the bound was read whole.</summary>
    [Theory]
    [InlineData(MaximumInspectedLeases, false)]
    [InlineData(MaximumInspectedLeases - 1, true)]
    public async Task RetireAsync_LeasesThatAllKnowRetention_ProceedsOnlyBelowTheBound(int leases, bool proceeds)
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Age([]);
        fixture.Leases.ReadEveryHeldAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<WorkLease>>(
            [
                .. Enumerable.Range(0, leases).Select(index => new WorkLease(
                    WorkScope.Create($"mail-account:account-{index}"),
                    WorkLeaseHolder.ForBuild("0.9.0"),
                    ReplicaIdentity.Create("replica-2"),
                    Now.AddMinutes(1))),
            ]));

        // Act
        await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        await fixture.Sessions.Received(proceeds ? 1 : 0).BeginSessionAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>An account a reload stopped declaring has no answer for what its deletes do, and none invented here would be it.</summary>
    [Fact]
    public async Task RetireAsync_AnAccountNoLongerDeclaringItsDeletes_DoesNothing()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Dispositions.GetAuthoredDeleteDisposition(Account).Returns(
            _ => throw new InvalidOperationException("No such account."));

        // Act
        var retired = await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        Assert.Equal(0, retired);
        await fixture.Sessions.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Each full batch reads on from where the last ended, so a row this run could not retire is not read twice, and the night's bound stops it.</summary>
    [Fact]
    public async Task RetireAsync_FullBatches_ResumePastTheLastRowAndStopAtTheBound()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Held);
        var batches = 0;
        fixture.AgedMail.ReadAsync(
                Arg.Any<IPersistenceSession>(),
                Account,
                Arg.Any<MailMessageRetention>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                batches++;

                return Task.FromResult<IReadOnlyList<AgedStoredMail>>(
                [
                    .. Enumerable.Range(0, call.ArgAt<int>(5)).Select(_ =>
                        new AgedStoredMail(StoredEmailId.Create(Guid.CreateVersion7()), InboxRow, Inbox.Alias, Occurrence: null)),
                ]);
            });

        // Act
        var retired = await fixture.Retirement.RetireAsync(Account, Token);

        // Assert
        Assert.Equal(MailMessageRetirement.MaximumMessagesReadPerNight, retired);
        Assert.Equal(MailMessageRetirement.MaximumMessagesReadPerNight / MailMessageRetirement.MessagesRetiredPerTransaction, batches);
        await fixture.AgedMail.Received(batches - 1).ReadAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<MailMessageRetention>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Is<StoredEmailId?>(resumeAfter => resumeAfter != null),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    private static AgedStoredMail Scheduled(uint uid) => new(
        StoredEmailId.Create(Guid.CreateVersion7()),
        InboxRow,
        Inbox.Alias,
        EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(1), ImapUid.Create(uid)));

    private sealed class Fixture
    {
        public Fixture(ClientSignals? signals = null, InMemoryLocalMailFolderStore? folders = null)
        {
            this.Sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(this.Session);
            this.Session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            this.MessageRetention.GetMessageRetention(Account)
                .Returns(new MailMessageRetention(IsEnabled: true, TimeSpan.FromDays(730), ["Archive"]));
            this.Dispositions.GetAuthoredDeleteDisposition(Account).Returns(AuthoredDeleteEmailDisposition.RetainTombstone);
            this.Dispositions.GetAuthoredDeleteServerDisposition(Account).Returns(AuthoredDeleteServerDisposition.FlagDeleted);
            this.Mappings.FindFolderNamed(Account, Inbox.Alias).Returns(
                MailFolderMapping.ToRemotePath(Inbox.Alias, Inbox.RemotePath));
            this.Leases.ReadEveryHeldAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WorkLease>>([]));
            this.ReturnsCustody(MailAccountCustodyPhase.Mirrored);
            this.Retirement = new MailMessageRetirement(
                this.MessageRetention,
                this.Sessions,
                this.Custody,
                this.AgedMail,
                this.Dispositions,
                this.Mappings,
                MailboxChangeSubmissions.Over(this.Records, localFolders: folders, states: this.States, signals: signals),
                this.States,
                this.Leases,
                new FakeTimeProvider(Now));
        }

        public InMemoryMailboxMutationRecordStore Records { get; } = new();

        public InMemoryLocalEmailStateStore States { get; } = new(Account);

        public IMailMessageRetentionReader MessageRetention { get; } = Substitute.For<IMailMessageRetentionReader>();

        public IPersistenceSessionFactory Sessions { get; } = Substitute.For<IPersistenceSessionFactory>();

        public IPersistenceSession Session { get; } = Substitute.For<IPersistenceSession>();

        public IMailAccountCustodyStore Custody { get; } = Substitute.For<IMailAccountCustodyStore>();

        public IAgedStoredMailReader AgedMail { get; } = Substitute.For<IAgedStoredMailReader>();

        public IAuthoredDeleteEmailDispositionReader Dispositions { get; } = Substitute.For<IAuthoredDeleteEmailDispositionReader>();

        public IMailFolderMappingReader Mappings { get; } = Substitute.For<IMailFolderMappingReader>();

        public IWorkLeaseStore Leases { get; } = Substitute.For<IWorkLeaseStore>();

        public MailMessageRetirement Retirement { get; }

        public void ReturnsCustody(MailAccountCustodyPhase phase) =>
            this.Custody.ReadAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<CancellationToken>())
                .Returns(new MailAccountCustodyState(MailAccountCustody.MirrorSource, phase));

        public void Age(IReadOnlyList<AgedStoredMail> mail) => this.AgedMail.ReadAsync(
                Arg.Any<IPersistenceSession>(),
                Account,
                Arg.Any<MailMessageRetention>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mail));
    }
}
