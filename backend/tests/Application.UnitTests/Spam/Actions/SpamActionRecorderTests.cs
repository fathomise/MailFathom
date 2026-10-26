// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Actions;

public sealed class SpamActionRecorderTests
{
    private static readonly StoredEmailId Email =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000090a0"));

    private static readonly MailAccountId Account =
        MailAccountId.Create("acct-1");

    private static readonly MailAccountId AnotherAccount = MailAccountId.Create("acct-2");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailTransportSecurityPolicy TlsOnConnect = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemorySpamVerdictActStore verdictActs = new();

    private readonly InMemoryMailFolderResolutionStore bindings = new();

    private readonly StubMailFolderMappings mappings = StubMailFolderMappings.Nothing;

    private readonly List<RemoteFolder> advertisedFolders = [];

    private readonly IAuthoredDeleteEmailDispositionReader dispositions =
        Substitute.For<IAuthoredDeleteEmailDispositionReader>();

    /// <summary>The reader the recorder last asked, kept so a test can assert whose switches were read.</summary>
    private ISpamActionSettingsReader? settingsReader;

    /// <summary>Arranges the answer every test shares, so a test that cares about it overrides it afterwards.</summary>
    public SpamActionRecorderTests() => this.dispositions
        .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
        .Returns(AuthoredDeleteEmailDisposition.RetainLocalCopy);

    /// <summary>
    /// A mailbox that asked for no action costs the occurrence row and nothing else: the switches are the account's,
    /// and the occurrence is what names the account the message sits in.
    /// </summary>
    [Fact]
    public async Task RecordAsync_NeitherSwitchOn_AsksForNothingAndOpensNoRecord()
    {
        // Arrange
        var recorder = this.Recorder(SpamActionSettings.None);

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NoActionConfigured, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>What happens to junk is the mailbox's own setting, so the switches read are the account the message sits in.</summary>
    /// <remarks>
    /// The identity this whole per-account split exists for, and it is driven with an account <see cref="Account" />
    /// is not so that the resolved value is separable from this class's own. A recorder reading a constant, or taking
    /// the settings off the acting user, answers with the settings arranged for no mailbox and files nothing — which
    /// on a user holding several is mail moved and marked read on one server under another mailbox's switches.
    /// </remarks>
    [Fact]
    public async Task RecordAsync_AVerdictOnOneAccount_ReadsThatAccountsActionSettings()
    {
        // Arrange
        this.MapMirroredJunk("Junk");
        this.mappings.With(
            AnotherAccount,
            MailFolderMapping.ToSpecialUse(Junk, MailFolderSpecialUse.Junk, MailFolderParticipation.Full));
        this.bindings.Bind(AnotherAccount, Junk, "Junk");

        var recorder = this.Recorder(
            FilingAndMarkingRead(),
            ReaderOf(OccurrenceIn(Inbox, isRemotelySeen: false, account: AnotherAccount)),
            settingsOf: AnotherAccount);

        // Act
        var result = await recorder.RecordAsync(
            SpamVerdictOf(SpamVerdict.Spam),
            SpamActionPosture.Acting,
            TestContext.Current.CancellationToken);

        // Assert
        var settingsReader = this.settingsReader!;

        settingsReader.Received().ActionsFor(AnotherAccount);
        Assert.Equal(SpamActionOutcome.Requested, result.Outcome);
    }

    [Theory]
    [InlineData(SpamVerdict.NotSpam)]
    [InlineData(SpamVerdict.Undetermined)]
    public async Task RecordAsync_AVerdictThatIsNotSpam_AsksForNothing(SpamVerdict verdict)
    {
        // Arrange
        var recorder = this.Recorder(FilingAndMarkingRead());

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(verdict), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NotSpam, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    [Fact]
    public async Task RecordAsync_BothSwitchesOn_MarksReadBeforeItRelocates()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var recorder = this.Recorder(FilingAndMarkingRead());

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        MailboxMutation[] appliedInOrder = [MailboxMutation.SetSeen, MailboxMutation.Relocate];
        MailboxMutation[] recorded = [.. this.records.OpenedRequests.Select(request => request.Mutation)];

        Assert.Equal(SpamActionOutcome.Requested, result.Outcome);
        Assert.Equal<IReadOnlyList<MailboxMutation>>(appliedInOrder, recorded);
        Assert.NotNull(result.MarkedReadRecordId);
        Assert.NotNull(result.FiledRecordId);
    }

    [Fact]
    public async Task RecordAsync_FilingIntoAMirroredFolder_AsksForARelocationThatKeepsTheLocalRow()
    {
        // Arrange
        this.MapMirroredJunk("Spam");
        var recorder = this.Recorder(SpamActionSettings.Create(filesJunk: true, marksJunkRead: false));

        // Act
        await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.Relocate, request.Mutation);
        Assert.Equal("Spam", request.DestinationPath?.Value);
        Assert.Null(request.LocalDisposition);
    }

    /// <summary>An unmirrored junk folder is the recommended destination, and the account's own answer decides what is kept.</summary>
    [Fact]
    public async Task RecordAsync_FilingIntoAnUnmirroredFolder_CarriesTheAccountsLocalDisposition()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        this.dispositions
            .GetAuthoredDeleteDisposition(Account)
            .Returns(AuthoredDeleteEmailDisposition.EraseLocalCopy);
        var recorder = this.Recorder(SpamActionSettings.Create(filesJunk: true, marksJunkRead: false));

        // Act
        await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(AuthoredDeleteEmailDisposition.EraseLocalCopy, request.LocalDisposition);
    }

    [Fact]
    public async Task RecordAsync_AMessageAlreadyReportedRead_DoesNotWriteTheFlagAgain()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var recorder = this.Recorder(FilingAndMarkingRead(), OccurrenceIn(Inbox, isRemotelySeen: true));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.MarkedReadRecordId);
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.Relocate, request.Mutation);
    }

    [Fact]
    public async Task RecordAsync_AMessageAlreadyInTheDestination_DoesNotRelocateItIntoItself()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var recorder = this.Recorder(FilingAndMarkingRead(), OccurrenceIn(Junk, isRemotelySeen: false));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.FiledRecordId);
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.SetSeen, request.Mutation);
    }

    [Fact]
    public async Task RecordAsync_EverythingTheSwitchesAskForAlreadyTrue_AsksForNothing()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var recorder = this.Recorder(FilingAndMarkingRead(), OccurrenceIn(Junk, isRemotelySeen: true));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NothingToChange, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>The rule that keeps filing from arguing with the user: one undo is enough, and nothing files it again.</summary>
    [Fact]
    public async Task RecordAsync_AMessageFiledOnceAndSinceMovedBackOut_IsLeftAloneEntirely()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var settings = FilingAndMarkingRead();
        var recorder = this.Recorder(settings);
        await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // The user drags the message back into the inbox, which reaches this instance as a new occurrence of the same
        // local email under a new UID.
        var movedBack = this.Recorder(settings, OccurrenceIn(Inbox, isRemotelySeen: false, uid: 4402));

        // Act
        var result = await movedBack.RecordAsync(
            SpamVerdictOf(SpamVerdict.Spam),
            SpamActionPosture.Acting,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.PreviouslyFiled, result.Outcome);
        Assert.Equal(2, this.records.OpenedRecordCount);
    }

    /// <summary>The same reading makes a filing still in flight idempotent, which is what a re-classification hits.</summary>
    [Fact]
    public async Task RecordAsync_AFilingAlreadyAskedForAndNotYetCarriedOut_AsksForNothingFurther()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var recorder = this.Recorder(SpamActionSettings.Create(filesJunk: true, marksJunkRead: false));
        await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.PreviouslyFiled, result.Outcome);
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    [Fact]
    public async Task RecordAsync_TheSameVerdictUnderTheSameProfileTwice_AsksForOneChange()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var recorder = this.Recorder(SpamActionSettings.Create(filesJunk: false, marksJunkRead: true));

        // Act
        await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);
        await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    [Fact]
    public async Task RecordAsync_AnOccurrenceNothingIsStoredFor_ReportsItGoneRatherThanFailing()
    {
        // Arrange
        var occurrences = Substitute.For<ISpamActionOccurrenceReader>();
        occurrences
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns((SpamActionOccurrence?)null);
        var recorder = this.Recorder(FilingAndMarkingRead(), occurrences);

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.OccurrenceMissing, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>Marking spam read while it stays in the inbox is worse than waiting, so an unresolvable filing holds both back.</summary>
    [Fact]
    public async Task RecordAsync_ADestinationTheAccountNoLongerMaps_WithholdsTheFlagChangeToo()
    {
        // Arrange
        var recorder = this.Recorder(FilingAndMarkingRead());

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.DestinationUnresolved, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    [Fact]
    public async Task RecordAsync_ADestinationNothingHasBoundYet_WithholdsBothChanges()
    {
        // Arrange
        this.mappings.With(
            Account,
            MailFolderMapping.ToSpecialUse(Junk, MailFolderSpecialUse.Junk, MailFolderParticipation.Full));
        var recorder = this.Recorder(FilingAndMarkingRead());

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.DestinationUnresolved, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A reload can withdraw an account mid-run, and what it kept of mail it files away is then nobody's to guess.</summary>
    [Fact]
    public async Task RecordAsync_AnAccountAReloadHasWithdrawn_WithholdsBothChangesRatherThanFailing()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        this.dispositions
            .GetAuthoredDeleteDisposition(Account)
            .Throws(new InvalidOperationException("The account is no longer configured."));
        var recorder = this.Recorder(FilingAndMarkingRead());

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.AccountNoLongerConfigured, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    [Fact]
    public async Task RecordAsync_AnExplicitlyNamedJunkFolder_FilesIntoThatFolderRatherThanTheRole()
    {
        // Arrange
        var elsewhere = MailFolderAlias.Create("QUARANTINE");
        this.mappings.With(
            Account,
            MailFolderMapping.ToRemotePath(elsewhere, RemoteFolderPath.Create("Quarantine")));
        this.bindings.Bind(Account, elsewhere, "Quarantine");
        var recorder = this.Recorder(SpamActionSettings.Create(
            filesJunk: true,
            marksJunkRead: false,
            MailFolderReference.ToAlias(elsewhere)));

        // Act
        await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal("Quarantine", request.DestinationPath?.Value);
    }

    /// <summary>
    /// On a held account a filing no local folder corresponds to is refused after the read change was already made in
    /// the same transaction, so the verdict ends unresolved and that transaction is never committed.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AHeldFilingWithNoLocalFolder_WithholdsTheReadChangeByNotCommitting()
    {
        // Arrange
        var elsewhere = MailFolderAlias.Create("QUARANTINE");
        this.mappings.With(
            Account,
            MailFolderMapping.ToRemotePath(elsewhere, RemoteFolderPath.Create("Quarantine")));
        this.bindings.Bind(Account, elsewhere, "Quarantine");

        var states = new InMemoryLocalEmailStateStore(Account);
        states.Store(
            Email,
            new LocalEmailState(
                MailFolderResolution.FirstBindingOf(Inbox, RemoteFolderPath.Create("INBOX")),
                HoldsSourceOccurrence: true,
                Folder: null,
                IsSeen: false,
                IsFlagged: false,
                RemoteEmailKeywords.Create([])));
        var session = Substitute.For<IPersistenceSession>();
        var recorder = this.Recorder(
            SpamActionSettings.Create(filesJunk: true, marksJunkRead: true, MailFolderReference.ToAlias(elsewhere)),
            submission: MailboxChangeSubmissions.Over(
                this.records,
                new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Held),
                states),
            session: session);

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.DestinationUnresolved, result.Outcome);
        await session.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A restoring account files and marks read at once, and the source is still owed both: the answer is applied and
    /// names the records the convergence pass will carry, rather than reporting an act nothing will ever tell the
    /// server about.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ARestoringAccountHoldingAnOccurrence_AppliesBothChangesAndNamesTheRecordsTheSourceIsOwed()
    {
        // Arrange
        var junk = MailFolderAlias.Create("JUNK");
        this.mappings.With(Account, MailFolderMapping.ToRemotePath(junk, RemoteFolderPath.Create("Junk")));
        this.bindings.Bind(Account, junk, "Junk");

        var states = new InMemoryLocalEmailStateStore(Account);
        states.Store(
            Email,
            new LocalEmailState(
                MailFolderResolution.FirstBindingOf(Inbox, RemoteFolderPath.Create("INBOX")),
                HoldsSourceOccurrence: true,
                Folder: null,
                IsSeen: false,
                IsFlagged: false,
                RemoteEmailKeywords.Create([])));
        var localFolders = new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Restoring);
        await localFolders.SaveAsync(
            Substitute.For<IPersistenceSession>(),
            Account,
            [
                new LocalMailFolder(
                    MailFolderId.Create(Guid.CreateVersion7()),
                    ParentId: null,
                    LocalMailFolderName.Create("Junk"),
                    Role: null,
                    SourceFolderAlias: junk),
            ],
            [],
            TestContext.Current.CancellationToken);
        var recorder = this.Recorder(
            SpamActionSettings.Create(filesJunk: true, marksJunkRead: true, MailFolderReference.ToAlias(junk)),
            submission: MailboxChangeSubmissions.Over(this.records, localFolders, states));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.Applied, result.Outcome);
        Assert.NotNull(result.MarkedReadRecordId);
        Assert.NotNull(result.FiledRecordId);
        Assert.True(states.States[Email].IsSeen);
    }

    [Fact]
    public async Task RecordAsync_AnyRequest_NamesTheClassificationAsWhatAskedForIt()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var recorder = this.Recorder(SpamActionSettings.Create(filesJunk: true, marksJunkRead: false));

        // Act
        await recorder.RecordAsync(
            SpamVerdictOf(SpamVerdict.Spam),
            SpamActionPosture.Acting,
            TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutationOrigin.Classification, request.Requester.Origin);
        Assert.Equal("Deterministic@none", request.Requester.Identity);
    }

    /// <summary>A dry run is a rehearsal of the decision an acting run would take, and it writes nothing down.</summary>
    [Fact]
    public async Task RecordAsync_ADryRunOverMailThatWouldBeFiled_ReportsTheDecisionAndOpensNoRecord()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var recorder = this.Recorder(FilingAndMarkingRead());

        // Act
        var result = await recorder.RecordAsync(
            SpamVerdictOf(SpamVerdict.Spam),
            SpamActionPosture.DryRun,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.WouldRequest, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>The posture is read last, so every reason to leave a message alone reports identically under both.</summary>
    [Fact]
    public async Task RecordAsync_ADryRunOverMailAlreadyFiled_ReportsThatRatherThanWhatItWouldHaveDone()
    {
        // Arrange
        this.MapMirroredJunk("Spam");
        var recorder = this.Recorder(FilingAndMarkingRead());

        await recorder.RecordAsync(
            SpamVerdictOf(SpamVerdict.Spam),
            SpamActionPosture.Acting,
            TestContext.Current.CancellationToken);

        var alreadyOpened = this.records.OpenedRecordCount;

        // Act
        var result = await recorder.RecordAsync(
            SpamVerdictOf(SpamVerdict.Spam),
            SpamActionPosture.DryRun,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.PreviouslyFiled, result.Outcome);
        Assert.Equal(alreadyOpened, this.records.OpenedRecordCount);
    }

    [Fact]
    public async Task RecordAsync_ADryRunOverMailNoSwitchReaches_ReportsWhyRatherThanADryRun()
    {
        // Arrange
        var recorder = this.Recorder(FilingAndMarkingRead());

        // Act
        var result = await recorder.RecordAsync(
            SpamVerdictOf(SpamVerdict.NotSpam),
            SpamActionPosture.DryRun,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NotSpam, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    [Fact]
    public async Task RecordAsync_APostureOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange
        var recorder = this.Recorder(FilingAndMarkingRead());

        // Act
        var refusal = async () => await recorder.RecordAsync(
            SpamVerdictOf(SpamVerdict.Spam),
            (SpamActionPosture)7,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(refusal);
    }

    [Fact]
    public async Task RecordAsync_NoClassification_IsRefused()
    {
        // Arrange
        var recorder = this.Recorder(FilingAndMarkingRead());

        // Act
        var refusal = async () => await recorder.RecordAsync(
            classification: null!,
            SpamActionPosture.Acting,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ArgumentNullException>(refusal);
    }

    /// <summary>A spam verdict on a mirrored account asks for <c>$Junk</c> under an identity naming it, and records that it did.</summary>
    [Fact]
    public async Task RecordAsync_TheKeywordSwitchOnASpamVerdict_AsksForJunkAndRecordsTheAct()
    {
        // Arrange
        var recorder = this.Recorder(MarkingWithKeywords());

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.Requested, result.Outcome);
        Assert.NotNull(result.KeywordsRecordId);
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.AddKeywords, request.Mutation);
        Assert.Equal(["$Junk"], request.Keywords!.Values);
        Assert.Equal("Deterministic@$Junk", request.Requester.Identity);
        Assert.Equal([SpamVerdictAct.MarkedJunk], this.verdictActs.Of(Email));
    }

    /// <summary>The phishing subcategory adds <c>$Phishing</c> beside <c>$Junk</c>, in the one request, never alone.</summary>
    [Fact]
    public async Task RecordAsync_APhishingVerdict_AsksForJunkAndPhishingTogether()
    {
        // Arrange
        var recorder = this.Recorder(MarkingWithKeywords());

        // Act
        await recorder.RecordAsync(PhishingVerdict(), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(["$Junk", "$Phishing"], request.Keywords!.Values.Order(StringComparer.Ordinal));
        Assert.Equal(
            [SpamVerdictAct.MarkedJunk, SpamVerdictAct.MarkedPhishing],
            this.verdictActs.Of(Email).Order());
    }

    /// <summary>A keyword the message already carries is not written, whoever set it.</summary>
    [Fact]
    public async Task RecordAsync_AMessageAlreadyCarryingJunk_AsksForNoKeyword()
    {
        // Arrange
        var recorder = this.Recorder(
            MarkingWithKeywords(),
            OccurrenceIn(Inbox, isRemotelySeen: false, keywords: ["$junk"]));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NothingToChange, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A <c>$Junk</c> a verdict wrote once and the person took off is not written again, and no <c>$Phishing</c> stands alone.</summary>
    [Fact]
    public async Task RecordAsync_AJunkKeywordThePersonRemoved_IsNotWrittenAgainAndNoPhishingStandsAlone()
    {
        // Arrange
        this.verdictActs.With(Email, SpamVerdictAct.MarkedJunk);
        var recorder = this.Recorder(MarkingWithKeywords());

        // Act
        var result = await recorder.RecordAsync(PhishingVerdict(), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NothingToChange, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// A message later named phishing under the same stage gets <c>$Phishing</c> as a request of its own, rather than
    /// reading as a repeat of the <c>$Junk</c> the same occurrence was given before.
    /// </summary>
    [Fact]
    public async Task RecordAsync_PhishingNamedAfterJunkWasWritten_AsksForPhishingAsANewRequest()
    {
        // Arrange
        var recorder = this.Recorder(MarkingWithKeywords());
        await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);
        var carryingJunk = this.Recorder(
            MarkingWithKeywords(),
            OccurrenceIn(Inbox, isRemotelySeen: false, keywords: ["$Junk"]));

        // Act
        var result = await carryingJunk.RecordAsync(PhishingVerdict(), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.Requested, result.Outcome);
        Assert.Equal(2, this.records.OpenedRecordCount);
        Assert.Equal(["$Phishing"], this.records.OpenedRequests[^1].Keywords!.Values);
    }

    /// <summary>The account's <c>WriteKeywords</c> governs a verdict's keywords as it governs a rule's.</summary>
    [Fact]
    public async Task RecordAsync_AnAccountThatPermitsNoKeywords_WritesNone()
    {
        // Arrange
        var recorder = this.Recorder(
            MarkingWithKeywords(),
            permissions: MailRuleActionPermissions.Default with { PermitsWriteKeywords = false });

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NothingToChange, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// The read change comes first and the filing last, with the keywords between, each its own record — so a folder that
    /// refuses the keyword as <c>MailboxMutationUnsupported</c> refuses that record alone, and the keywords are aimed at
    /// the occurrence that exists before the move.
    /// </summary>
    [Fact]
    public async Task RecordAsync_EverySwitchOn_MarksReadThenSetsTheKeywordsThenFilesAsSeparateRecords()
    {
        // Arrange
        this.MapUnmirroredJunk("Spam");
        var recorder = this.Recorder(SpamActionSettings.Create(
            filesJunk: true,
            marksJunkRead: true,
            marksJunkWithKeywords: true));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [MailboxMutation.SetSeen, MailboxMutation.AddKeywords, MailboxMutation.Relocate],
            this.records.OpenedRequests.Select(request => request.Mutation));
        Assert.NotNull(result.MarkedReadRecordId);
        Assert.NotNull(result.KeywordsRecordId);
        Assert.NotNull(result.FiledRecordId);
        Assert.Equal(
            [SpamVerdictAct.FiledIntoJunk, SpamVerdictAct.MarkedJunk],
            this.verdictActs.Of(Email).Order());
    }

    /// <summary>No verdict writes <c>$NotJunk</c>: one that is not spam asks for nothing, the keyword switch on or not.</summary>
    [Theory]
    [InlineData(SpamVerdict.NotSpam)]
    [InlineData(SpamVerdict.Undetermined)]
    public async Task RecordAsync_AVerdictThatIsNotSpamWithTheKeywordSwitchOn_WritesNoKeyword(SpamVerdict verdict)
    {
        // Arrange
        var recorder = this.Recorder(MarkingWithKeywords());

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(verdict), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NotSpam, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A message somebody marked both <c>$Junk</c> and <c>$NotJunk</c> has both removed, as RFC 9051 asks of a client.</summary>
    [Fact]
    public async Task RecordAsync_AVerdictRestingOnJunkAndNotJunkTogether_RemovesBoth()
    {
        // Arrange
        var recorder = this.Recorder(
            MarkingWithKeywords(),
            OccurrenceIn(Inbox, isRemotelySeen: false, keywords: ["$Junk", "$NotJunk"]));

        // Act
        var result = await recorder.RecordAsync(CancellingVerdict(), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.Requested, result.Outcome);
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.RemoveKeywords, request.Mutation);
        Assert.Equal(["$Junk", "$NotJunk"], request.Keywords!.Values.Order(StringComparer.Ordinal));
        Assert.Equal([SpamVerdictAct.ClearedJunkMarks], this.verdictActs.Of(Email));
    }

    /// <summary>A pair somebody else set is cleared on a spam verdict too, in place of the <c>$Junk</c> it would otherwise owe.</summary>
    [Fact]
    public async Task RecordAsync_ASpamVerdictOverJunkAndNotJunkTogether_RemovesBothAndAddsNothing()
    {
        // Arrange
        var recorder = this.Recorder(
            MarkingWithKeywords(),
            OccurrenceIn(Inbox, isRemotelySeen: false, keywords: ["$Junk", "$NotJunk"]));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.Requested, result.Outcome);
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.RemoveKeywords, request.Mutation);
        Assert.Equal(["$Junk", "$NotJunk"], request.Keywords!.Values.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The person's <c>$NotJunk</c> beside a <c>$Junk</c> this deployment wrote is the correction, so only the verdict's own
    /// keyword is taken off and the correction stands.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ThePersonsNotJunkBesideTheVerdictsOwnJunk_TakesOffTheJunkAlone()
    {
        // Arrange
        this.verdictActs.With(Email, SpamVerdictAct.MarkedJunk);
        var recorder = this.Recorder(
            MarkingWithKeywords(),
            OccurrenceIn(Inbox, isRemotelySeen: false, keywords: ["$Junk", "$NotJunk"]));

        // Act
        var result = await recorder.RecordAsync(CorrectedVerdict(), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.Requested, result.Outcome);
        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.RemoveKeywords, request.Mutation);
        Assert.Equal(["$Junk"], request.Keywords!.Values);
    }

    /// <summary>A pair is cleared once per message, so one somebody sets again afterwards is left to them in every phase.</summary>
    [Fact]
    public async Task RecordAsync_APairClearedOnceAndSetAgain_IsLeftAlone()
    {
        // Arrange
        this.verdictActs.With(Email, SpamVerdictAct.ClearedJunkMarks);
        var recorder = this.Recorder(
            MarkingWithKeywords(),
            OccurrenceIn(Inbox, isRemotelySeen: false, keywords: ["$Junk", "$NotJunk"]));

        // Act
        var result = await recorder.RecordAsync(CancellingVerdict(), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NotSpam, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>Removing the cancelled pair is the keyword switch's alone, so an account that left it off is told the verdict was not spam.</summary>
    [Fact]
    public async Task RecordAsync_JunkAndNotJunkTogetherWithTheKeywordSwitchOff_RemovesNothing()
    {
        // Arrange
        var recorder = this.Recorder(
            FilingAndMarkingRead(),
            OccurrenceIn(Inbox, isRemotelySeen: false, keywords: ["$Junk", "$NotJunk"]));

        // Act
        var result = await recorder.RecordAsync(CancellingVerdict(), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NotSpam, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>On a held account the keyword commits to the stored keyword set, and the act is recorded for the next verdict to read.</summary>
    [Fact]
    public async Task RecordAsync_AHeldAccount_CommitsTheJunkKeywordLocallyAndRecordsTheAct()
    {
        // Arrange
        var states = StatesOfAMessageInTheInbox();
        var recorder = this.Recorder(
            MarkingWithKeywords(),
            submission: MailboxChangeSubmissions.Over(
                this.records,
                new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Held),
                states));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.Applied, result.Outcome);
        Assert.Null(result.KeywordsRecordId);
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Equal(["$JUNK"], states.States[Email].Keywords.Values);
        Assert.Equal([SpamVerdictAct.MarkedJunk], this.verdictActs.Of(Email));
    }

    /// <summary>A restoring account commits the keyword locally and opens the record that carries it to the source.</summary>
    [Fact]
    public async Task RecordAsync_ARestoringAccount_CommitsTheJunkKeywordAndNamesTheRecordTheSourceIsOwed()
    {
        // Arrange
        var states = StatesOfAMessageInTheInbox();
        var recorder = this.Recorder(
            MarkingWithKeywords(),
            submission: MailboxChangeSubmissions.Over(
                this.records,
                new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Restoring),
                states));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.Applied, result.Outcome);
        Assert.NotNull(result.KeywordsRecordId);
        Assert.Equal(MailboxMutation.AddKeywords, Assert.Single(this.records.OpenedRequests).Mutation);
        Assert.Equal(["$JUNK"], states.States[Email].Keywords.Values);
    }

    /// <summary>
    /// A held message a verdict filed and the person moved back out of the local junk folder is left alone, read from the
    /// record of what the verdict did — a held filing leaves no mutation record to read it from.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AHeldMessageFiledOnceAndMovedBackOut_IsLeftAlone()
    {
        // Arrange
        this.MapMirroredJunk("Junk");
        this.verdictActs.With(Email, SpamVerdictAct.FiledIntoJunk);
        var localFolders = new InMemoryLocalMailFolderStore(Account, MailAccountCustodyPhase.Held);
        var recorder = this.Recorder(
            FilingAndMarkingRead(),
            submission: MailboxChangeSubmissions.Over(this.records, localFolders, StatesOfAMessageInTheInbox()));

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.Acting, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.PreviouslyFiled, result.Outcome);
    }

    /// <summary>A dry run reaches the keyword decision the acting run would, and writes neither the change nor the act.</summary>
    [Fact]
    public async Task RecordAsync_ADryRunOverMailThatWouldBeMarked_ReportsTheDecisionAndRecordsNothing()
    {
        // Arrange
        var recorder = this.Recorder(MarkingWithKeywords());

        // Act
        var result = await recorder.RecordAsync(SpamVerdictOf(SpamVerdict.Spam), SpamActionPosture.DryRun, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.WouldRequest, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Empty(this.verdictActs.Of(Email));
    }

    private static SpamActionSettings MarkingWithKeywords() =>
        SpamActionSettings.Create(filesJunk: false, marksJunkRead: false, marksJunkWithKeywords: true);

    private static SpamClassification PhishingVerdict() => SpamClassification.Create(
        Email,
        SpamVerdict.Spam,
        SpamClassificationStage.Model,
        assessment: null,
        SpamClassificationProfile.Create(),
        [
            SpamSignal.Create(
                SpamSignalKind.ModelJudgement,
                "spam_kind",
                observation: "phishing",
                SpamSignalProvenance.FromModel("example/decider-2026-09-01", "3f9a1c")),
        ],
        EvaluatedAt,
        SpamSubcategory.Phishing);

    /// <summary>A verdict the deterministic stage reached past a <c>$Junk</c> and a <c>$NotJunk</c> somebody else set.</summary>
    private static SpamClassification CancellingVerdict() => SpamClassification.Create(
        Email,
        SpamVerdict.Undetermined,
        SpamClassificationStage.Deterministic,
        assessment: null,
        SpamClassificationProfile.Create(),
        [
            SpamSignal.Create(SpamSignalKind.JunkKeyword, "$NotJunk", observation: null, SpamSignalProvenance.FromMailboxKeyword("$NotJunk")),
            SpamSignal.Create(SpamSignalKind.JunkKeyword, "$Junk", observation: null, SpamSignalProvenance.FromMailboxKeyword("$Junk")),
        ],
        EvaluatedAt);

    /// <summary>A verdict the person's <c>$NotJunk</c> decided, the <c>$Junk</c> beside it being this deployment's own and no signal.</summary>
    private static SpamClassification CorrectedVerdict() => SpamClassification.Create(
        Email,
        SpamVerdict.NotSpam,
        SpamClassificationStage.Deterministic,
        assessment: null,
        SpamClassificationProfile.Create(),
        [
            SpamSignal.Create(SpamSignalKind.JunkKeyword, "$NotJunk", observation: null, SpamSignalProvenance.FromMailboxKeyword("$NotJunk")),
        ],
        EvaluatedAt);

    private static InMemoryLocalEmailStateStore StatesOfAMessageInTheInbox()
    {
        var states = new InMemoryLocalEmailStateStore(Account);
        states.Store(
            Email,
            new LocalEmailState(
                MailFolderResolution.FirstBindingOf(Inbox, RemoteFolderPath.Create("INBOX")),
                HoldsSourceOccurrence: true,
                Folder: null,
                IsSeen: false,
                IsFlagged: false,
                RemoteEmailKeywords.Create([])));

        return states;
    }

    private static SpamActionSettings FilingAndMarkingRead() =>
        SpamActionSettings.Create(filesJunk: true, marksJunkRead: true);

    private static SpamClassification SpamVerdictOf(SpamVerdict verdict) => SpamClassification.Create(
        Email,
        verdict,
        SpamClassificationStage.Deterministic,
        assessment: null,
        SpamClassificationProfile.Create(),
        [],
        EvaluatedAt);

    private static SpamActionOccurrence OccurrenceIn(
        MailFolderAlias folderAlias,
        bool isRemotelySeen,
        uint uid = 4401,
        MailAccountId? account = null,
        params string[] keywords) => new(
        Email,
        EmailOccurrenceId.Create(
            account ?? Account,
            new MailFolderResolutionId(folderAlias, MailFolderResolutionGeneration.First),
            ImapUidValidity.Create(9),
            ImapUid.Create(uid)),
        folderAlias,
        isRemotelySeen,
        RemoteEmailKeywords.Create(keywords));

    /// <summary>Maps the junk role onto a folder the account does not mirror, which is the recommended arrangement.</summary>
    /// <remarks>
    /// A folder nothing mirrors has no run to bind it, so it is resolved against what the server advertises at the moment
    /// a filing first needs it. Advertising it here is therefore part of the arrangement rather than a detail of it.
    /// </remarks>
    private void MapUnmirroredJunk(string remotePath)
    {
        this.mappings.With(
            Account,
            MailFolderMapping.ToSpecialUse(Junk, MailFolderSpecialUse.Junk, MailFolderParticipation.MappedOnly));
        this.advertisedFolders.Add(new RemoteFolder(
            RemoteFolderPath.Create(remotePath),
            [MailFolderSpecialUse.Junk]));
    }

    /// <summary>Maps the junk role onto a folder the account mirrors, and records the binding its own run would have.</summary>
    private void MapMirroredJunk(string remotePath)
    {
        this.mappings.With(
            Account,
            MailFolderMapping.ToSpecialUse(Junk, MailFolderSpecialUse.Junk, MailFolderParticipation.Full));
        this.bindings.Bind(Account, Junk, remotePath);
    }

    private SpamActionRecorder Recorder(
        SpamActionSettings settings,
        SpamActionOccurrence occurrence) =>
        this.Recorder(settings, ReaderOf(occurrence));

    /// <summary>Builds the recorder over a reader that answers with these settings for one account and with nothing for any other.</summary>
    /// <param name="settings">The switches the mailbox has set.</param>
    /// <param name="occurrences">Where the message sits, defaulting to an unread occurrence in the inbox.</param>
    /// <param name="settingsOf">The account those switches belong to, defaulting to this class's own.</param>
    /// <param name="submission">What the changes are submitted through, defaulting to one over an account that is not held.</param>
    /// <param name="session">The session every commit is staged in, defaulting to one that commits.</param>
    /// <param name="permissions">What the account lets a change author, defaulting to the permissions every account starts with.</param>
    /// <remarks>
    /// Configured per account rather than for any account, so a recorder forwarding a constant reads as no action taken
    /// and fails the test that expected one. Separating that constant from this class's own account needs
    /// <paramref name="settingsOf" /> as well, since every other test here drives the one account and the two values
    /// are otherwise one.
    /// </remarks>
    private SpamActionRecorder Recorder(
        SpamActionSettings settings,
        ISpamActionOccurrenceReader? occurrences = null,
        MailAccountId? settingsOf = null,
        MailboxChangeSubmission? submission = null,
        IPersistenceSession? session = null,
        MailRuleActionPermissions? permissions = null)
    {
        var settingsReader = Substitute.For<ISpamActionSettingsReader>();
        settingsReader.ActionsFor(Arg.Any<MailAccountId>()).Returns(SpamActionSettings.None);
        settingsReader.ActionsFor(settingsOf ?? Account).Returns(settings);
        this.settingsReader = settingsReader;

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => session ?? new CommittingSession());

        return new SpamActionRecorder(
            settingsReader,
            occurrences ?? ReaderOf(OccurrenceIn(Inbox, isRemotelySeen: false)),
            this.records,
            this.verdictActs,
            submission ?? MailboxChangeSubmissions.Over(this.records),
            this.DestinationResolver(sessionFactory),
            this.dispositions,
            PermissionsOf(permissions ?? MailRuleActionPermissions.Default),
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider(EvaluatedAt)));
    }

    /// <summary>Builds the one resolver every author of a filing reaches its destination through.</summary>
    private MailboxDestinationResolver DestinationResolver(IPersistenceSessionFactory sessionFactory)
    {
        var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
        remoteFolderCatalog
            .ListFoldersAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<RemoteFolder>>([.. this.advertisedFolders]));

        var transportSecurityPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
        transportSecurityPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(TlsOnConnect);

        return new MailboxCustodianComposition(new FakeTimeProvider(EvaluatedAt))
        {
            Mappings = this.mappings,
            FolderResolutions = this.bindings,
            RemoteFolderCatalog = remoteFolderCatalog,
            RemoteFolderCreator = Substitute.For<IRemoteFolderCreator>(),
            MappingChangeAuditor = Substitute.For<IMailFolderMappingChangeAuditor>(),
            PersistenceSessions = sessionFactory,
            Signals = ClientSignalPublishers.ReachingNobody,
            TransportPolicies = transportSecurityPolicies,
        }.Destinations;
    }

    private static IMailRuleActionPermissionReader PermissionsOf(MailRuleActionPermissions permissions)
    {
        var reader = Substitute.For<IMailRuleActionPermissionReader>();
        reader.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(permissions);

        return reader;
    }

    private static ISpamActionOccurrenceReader ReaderOf(SpamActionOccurrence occurrence)
    {
        var reader = Substitute.For<ISpamActionOccurrenceReader>();
        reader.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(occurrence);

        return reader;
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
