// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Coordination;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Filing;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Filing;

public sealed class CrossAccountFilerTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailAccountId Personal = MailAccountId.Create("personal");
    private static readonly StoredEmailId Filed = StoredEmailId.Create(Guid.Parse("0196f3a0-0000-7000-8000-00000000f11e"));
    private static readonly StoredEmailId Copy = StoredEmailId.Create(Guid.Parse("0196f3a0-0000-7000-8000-00000000c0c0"));
    private static readonly MailFolderResolution Inbox =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("inbox"), RemoteFolderPath.Create("INBOX"));
    private static readonly MailFolderResolution Clients =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("clients"), RemoteFolderPath.Create("Clients"));
    private static readonly MailFolderId ClientsFolder = MailFolderId.Create(Guid.Parse("0196f3a0-0000-7000-8000-0000000c11e7"));
    private static readonly MailboxMutationRequester Person = MailboxMutationRequester.Command("client-1");
    private static readonly AuthoredMailboxTarget Source = new(
        EmailOccurrenceId.Create(Work, Inbox.Id, ImapUidValidity.Create(3), ImapUid.Create(12)),
        Inbox,
        MailFolderId.Create(Guid.Parse("0196f3a0-0000-7000-8000-0000000001b0")));

    private readonly InMemoryMailAccountCustodyStore custody = new();
    private readonly InMemoryCrossAccountFilingStore filings = new();
    private readonly InMemoryMailboxMutationRecordStore records = new();
    private readonly InMemoryLocalEmailStateStore states = new(Work);
    private readonly IEmailMetadataRepository emails = Substitute.For<IEmailMetadataRepository>();
    private readonly IMailboxServerCapabilityStore capabilities = Substitute.For<IMailboxServerCapabilityStore>();

    public CrossAccountFilerTests()
    {
        this.states.Store(
            Filed,
            new LocalEmailState(Inbox, HoldsSourceOccurrence: true, Folder: null, IsSeen: true, IsFlagged: false, EmailKeywords.Create([])));
        this.emails
            .StoreLocalCopyAsync(default!, default, default!, default, default, default, default)
            .ReturnsForAnyArgs(Copy);
        this.capabilities.ReadAsync(default, default).ReturnsForAnyArgs(NamingWhereMessagesLand());
        this.records.HeldBackByFiling = this.filings.HoldsBack;
    }

    [Fact]
    public async Task CommitAsync_MirroredIntoMirrored_HidesTheSourceBehindARemovalTheAppendHoldsBack()
    {
        // Act
        var filed = await this.Filer().CommitAsync(Session(), Plan(sourceIsCarried: true, destinationIsCarried: true), Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(filed);
        var removal = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.Delete, removal.Mutation);
        Assert.Equal(AuthoredDeleteServerDisposition.Expunge, removal.ServerDisposition);
        Assert.Equal(AuthoredDeleteEmailDisposition.EraseLocalCopy, removal.LocalDisposition);
        var filing = this.filings.Single();
        Assert.Equal(this.records.RecordOf(removal).Id, filing.RemovalRecordId);
        Assert.False(filing.ErasesHeldSource);
        Assert.Equal(Copy, filing.DestinationEmailId);
        Assert.Equal(Clients.RemotePath, filing.DestinationFolderPath);
        Assert.Empty(await this.records.ReadOutstandingAsync(Work, 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitAsync_MirroredIntoHeld_LeavesTheRemovalFreeToGoAndRecordsNoAppend()
    {
        // Act
        await this.Filer().CommitAsync(Session(), Plan(sourceIsCarried: true, destinationIsCarried: false), Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.filings.Filings);
        var outstanding = Assert.Single(await this.records.ReadOutstandingAsync(Work, 10, TestContext.Current.CancellationToken));
        Assert.Equal(MailboxMutation.Delete, outstanding.Record.Request.Mutation);
    }

    [Fact]
    public async Task CommitAsync_HeldIntoMirrored_HidesTheSourceUntilTheAppendLandsAndErasesNothingYet()
    {
        // Act
        await this.Filer().CommitAsync(Session(), Plan(sourceIsCarried: false, destinationIsCarried: true), Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.records.OpenedRequests);
        var filing = this.filings.Single();
        Assert.True(filing.ErasesHeldSource);
        Assert.Null(filing.RemovalRecordId);
        Assert.Equal([Filed], this.filings.HiddenSources);
        Assert.Empty(this.states.Erased);
    }

    [Fact]
    public async Task CommitAsync_HeldIntoHeld_ErasesTheSourceInTheSameCommit()
    {
        // Act
        await this.Filer().CommitAsync(Session(), Plan(sourceIsCarried: false, destinationIsCarried: false), Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.records.OpenedRequests);
        Assert.Empty(this.filings.Filings);
        Assert.Equal([Filed], this.states.Erased);
    }

    [Fact]
    public async Task CommitAsync_AnyPairing_WritesTheDestinationCopyWithTheSourcesFlagsUnderTheDestinationsBinding()
    {
        // Act
        var filed = await this.Filer().CommitAsync(Session(), Plan(sourceIsCarried: true, destinationIsCarried: true), Person, TestContext.Current.CancellationToken);

        // Assert
        await this.emails.Received(1).StoreLocalCopyAsync(
            Arg.Any<IPersistenceSession>(),
            Personal,
            Clients.Id,
            Arg.Any<ExtractedEmailMetadata?>(),
            Arg.Any<long>(),
            new CopiedMailFlags(IsSeen: true, IsFlagged: false, EmailKeywords.Create([])),
            Arg.Any<CancellationToken>());
        Assert.Equal(new AppliedMailboxChange(Personal, ClientsFolder, Copy, Flags: null), filed!.Destination);
    }

    [Theory]
    [InlineData(MailAccountCustodyPhase.Restoring, MailAccountCustodyPhase.Mirrored, CrossAccountFilingRefusal.AccountRestoring)]
    [InlineData(MailAccountCustodyPhase.Mirrored, MailAccountCustodyPhase.Restoring, CrossAccountFilingRefusal.AccountRestoring)]
    public async Task PrepareAsync_EitherSideRestoring_IsRefusedBeforeAnythingIsWritten(
        MailAccountCustodyPhase source,
        MailAccountCustodyPhase destination,
        CrossAccountFilingRefusal expected)
    {
        // Arrange
        this.Place(Work, source);
        this.Place(Personal, destination);

        // Act
        var preparation = await this.PrepareAsync();

        // Assert
        Assert.Equal(expected, preparation.Refusal);
        Assert.Empty(this.filings.Filings);
    }

    [Fact]
    public async Task PrepareAsync_ASourceWithoutUidPlus_IsRefusedAsOneThatCannotLetTheMessageGo()
    {
        // Arrange
        this.capabilities.ReadAsync(Work, Arg.Any<CancellationToken>()).Returns(MailboxServerCapabilityObservation.NeverObserved);

        // Act
        var preparation = await this.PrepareAsync();

        // Assert
        Assert.Equal(CrossAccountFilingRefusal.SourceWithoutUidPlus, preparation.Refusal);
    }

    [Fact]
    public async Task PrepareAsync_ADestinationWithoutUidPlus_IsRefusedAsOneThatCannotSayWhichMessageItTook()
    {
        // Arrange
        this.capabilities.ReadAsync(Personal, Arg.Any<CancellationToken>()).Returns(MailboxServerCapabilityObservation.NeverObserved);

        // Act
        var preparation = await this.PrepareAsync();

        // Assert
        Assert.Equal(CrossAccountFilingRefusal.DestinationWithoutUidPlus, preparation.Refusal);
    }

    [Fact]
    public async Task PrepareAsync_ASentCopyAnOutgoingRecordFiled_IsRefused()
    {
        // Arrange
        this.filings.FileFromOutgoing(Filed);

        // Act
        var preparation = await this.PrepareAsync();

        // Assert
        Assert.Equal(CrossAccountFilingRefusal.FiledFromOutgoingRecord, preparation.Refusal);
    }

    [Fact]
    public async Task PrepareAsync_AHeldSourceWithoutUidPlus_IsNotRefused()
    {
        // Arrange
        this.Place(Work, MailAccountCustodyPhase.Held);
        this.capabilities.ReadAsync(Work, Arg.Any<CancellationToken>()).Returns(MailboxServerCapabilityObservation.NeverObserved);

        // Act
        var preparation = await this.PrepareAsync();

        // Assert
        Assert.Null(preparation.Refusal);
    }

    private static MailboxServerCapabilityObservation NamingWhereMessagesLand() =>
        MailboxServerCapabilityObservation.Observed(MailboxServerCapabilities.UidPlus, new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero));

    private static IPersistenceSession Session() => Substitute.For<IPersistenceSession>();

    private static CrossAccountFilingPlan Plan(bool sourceIsCarried, bool destinationIsCarried) => new(
        Source,
        Filed,
        Personal,
        Clients,
        ClientsFolder,
        new PreparedLocalCopy(Personal, Filed, Metadata: null, PlacedEmailContent.InDatabase("Subject: hi\r\n\r\nbody\r\n"u8.ToArray())),
        sourceIsCarried,
        destinationIsCarried);

    private void Place(MailAccountId account, MailAccountCustodyPhase phase) => this.custody.Place(
        account,
        phase switch
        {
            MailAccountCustodyPhase.Mirrored => MailAccountCustodyState.Mirrored,
            MailAccountCustodyPhase.Held => new MailAccountCustodyState(MailAccountCustody.HoldMailbox, phase),
            _ => new MailAccountCustodyState(MailAccountCustody.MirrorSource, phase),
        });

    private Task<CrossAccountFilingPreparation> PrepareAsync() =>
        this.Filer().PrepareAsync(Source, Filed, Personal, Clients.Alias, ClientsFolder, TestContext.Current.CancellationToken);

    private CrossAccountFiler Filer()
    {
        var bindings = Substitute.For<IMailFolderResolutionStore>();
        bindings.GetCurrentResolutionAsync(Personal, Clients.Alias, Arg.Any<CancellationToken>()).Returns(Clients);

        var composition = new MailboxCustodianComposition(new FakeTimeProvider())
        {
            Custody = this.custody,
            Leases = MailboxCustodianComposition.LeasesHeldBy(WorkLeaseHolder.ForBuild("0.9.0")),
            Capabilities = this.capabilities,
            Records = this.records,
            States = this.states,
            Emails = this.emails,
            FolderResolutions = bindings,
            CrossAccountFilings = this.filings,
        };

        return composition.CrossAccountFiler;
    }
}
