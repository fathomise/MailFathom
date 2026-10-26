// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Spam.Marking;
using MailFathom.Application.Synchronization;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Marking;

/// <summary>Covers the person's own spam verdict: what it records, the keywords that follow it, the move the account's switch adds, and every custody phase.</summary>
public sealed class SpamMarkRecorderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    private static readonly MailFolderAlias Sent = MailFolderAlias.Create("Sent");

    private static readonly MailFolderId InboxFolder = MailFolderId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000000a1"));

    private static readonly MailFolderId JunkFolder = MailFolderId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000000a2"));

    private static readonly MailFolderId SentFolder = MailFolderId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000000a3"));

    private static readonly StoredEmailId Email = StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000090b0"));

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("mark-1");

    private static readonly DateTimeOffset MarkedAt = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailTransportSecurityPolicy TlsOnConnect = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemoryEmailSpamClassificationStore classifications = new();

    private readonly InMemorySpamVerdictActStore verdictActs = new();

    private readonly InMemoryMailFolderResolutionStore bindings = new();

    private readonly StubMailFolderMappings mappings = StubMailFolderMappings.Nothing;

    private readonly IEmailChunkStore chunks = Substitute.For<IEmailChunkStore>();

    private readonly IAuthoredDeleteEmailDispositionReader dispositions = Substitute.For<IAuthoredDeleteEmailDispositionReader>();

    /// <summary>Maps the inbox and the junk folder as mirrored folders of the account, each bound where its run would have bound it.</summary>
    public SpamMarkRecorderTests()
    {
        this.mappings
            .With(Account, MailFolderMapping.ToSpecialUse(Inbox, MailFolderSpecialUse.Inbox, MailFolderParticipation.Full))
            .With(Account, MailFolderMapping.ToSpecialUse(Junk, MailFolderSpecialUse.Junk, MailFolderParticipation.Full))
            .With(Account, MailFolderMapping.ToSpecialUse(Sent, MailFolderSpecialUse.Sent, MailFolderParticipation.Full));
        this.bindings.Bind(Account, Inbox, "INBOX");
        this.bindings.Bind(Account, Junk, "Junk");
        this.bindings.Bind(Account, Sent, "Sent");
    }

    /// <summary>The person's verdict is recorded as theirs, with the subcategory they named, and the keywords follow it into the mailbox.</summary>
    [Fact]
    public async Task RecordAsync_SpamWithPhishingOnAMirroredAccount_RecordsThePersonsVerdictAndWritesBothJunkKeywords()
    {
        // Arrange
        var recorder = this.Recorder(Filing(off: true));

        // Act
        var result = await recorder.RecordAsync(
            Email,
            SpamMark.Spam(SpamSubcategory.Phishing),
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.Recorded, result.Outcome);
        Assert.False(result.IsMoved);

        var verdict = Assert.Single(this.classifications.Saved);
        Assert.Equal(
            (SpamVerdict.Spam, SpamClassificationStage.Person, SpamSubcategory.Phishing),
            (verdict.Verdict, verdict.DecidedBy, verdict.Subcategory));

        var request = Assert.Single(this.records.OpenedRequests);
        Assert.Equal(MailboxMutation.AddKeywords, request.Mutation);
        Assert.Equal(["$Junk", "$Phishing"], request.Keywords?.Values);
        Assert.Equal([SpamVerdictAct.MarkedJunk, SpamVerdictAct.MarkedPhishing], this.verdictActs.Of(Email).Order());
        await this.chunks.Received(1).DiscardChunksAsync(Arg.Any<IPersistenceSession>(), Email, Arg.Any<CancellationToken>());
    }

    /// <summary>Not spam is the person saying the message is ordinary mail, so it is written as <c>$NotJunk</c> and takes the junk keywords off.</summary>
    [Fact]
    public async Task RecordAsync_NotSpamOnAMessageCarryingTheJunkKeywords_AddsNotJunkAndTakesTheJunkKeywordsOff()
    {
        // Arrange
        var recorder = this.Recorder(Filing(off: true), keywords: ["$Junk", "$Phishing"]);

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.NotSpam, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.Recorded, result.Outcome);

        var verdict = Assert.Single(this.classifications.Saved);
        Assert.Equal((SpamVerdict.NotSpam, SpamClassificationStage.Person), (verdict.Verdict, verdict.DecidedBy));
        Assert.Equal(
            [MailboxMutation.AddKeywords, MailboxMutation.RemoveKeywords],
            this.records.OpenedRequests.Select(static opened => opened.Mutation));
        Assert.Equal(["$NotJunk"], this.records.OpenedRequests[0].Keywords?.Values);
        Assert.Equal(["$Junk", "$Phishing"], this.records.OpenedRequests[1].Keywords?.Values);
        Assert.Empty(this.verdictActs.Of(Email));
        await this.chunks.DidNotReceive().DiscardChunksAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<StoredEmailId>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Spam takes a <c>$NotJunk</c> off, because the person who once said otherwise is now saying spam.</summary>
    [Fact]
    public async Task RecordAsync_SpamOnAMessageCarryingNotJunk_TakesNotJunkOff()
    {
        // Arrange
        var recorder = this.Recorder(Filing(off: true), keywords: ["$NotJunk"]);

        // Act
        await recorder.RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [MailboxMutation.AddKeywords, MailboxMutation.RemoveKeywords],
            this.records.OpenedRequests.Select(static opened => opened.Mutation));
        Assert.Equal(["$NotJunk"], this.records.OpenedRequests[1].Keywords?.Values);
    }

    /// <summary>
    /// Repeating a mark is one change: the keywords the message already carries are not asked for again, and marking
    /// spam again with another subcategory replaces the person's subcategory rather than adding a second verdict.
    /// </summary>
    [Fact]
    public async Task RecordAsync_SpamAgainWithAnotherSubcategoryOnAMessageAlreadyMarked_ReplacesTheSubcategoryAndAsksForNothing()
    {
        // Arrange
        this.classifications.Hold(PersonsVerdict(SpamVerdict.Spam, SpamSubcategory.Scam));
        var recorder = this.Recorder(Filing(off: true), keywords: ["$Junk"]);

        // Act
        var result = await recorder.RecordAsync(
            Email,
            SpamMark.Spam(SpamSubcategory.Marketing),
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.Applied, result.Outcome);
        Assert.Empty(result.Records);
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Equal(SpamSubcategory.Marketing, Assert.Single(this.classifications.Saved).Subcategory);
    }

    /// <summary>The keywords are the person's own keyword acts, so an account that refuses keyword changes gets none, and the verdict still stands.</summary>
    [Fact]
    public async Task RecordAsync_AnAccountRefusingKeywordChanges_RecordsTheVerdictAndWritesNoKeyword()
    {
        // Arrange
        var recorder = this.Recorder(
            Filing(off: true),
            permissions: MailRuleActionPermissions.Default with { PermitsWriteKeywords = false });

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.Applied, result.Outcome);
        Assert.Single(this.classifications.Saved);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// With the account's switch on, spam is filed in the same act, after the keywords, and recorded as a verdict's
    /// filing so no verdict files it again once somebody moves it back out.
    /// </summary>
    [Fact]
    public async Task RecordAsync_SpamWithFilingOnAMirroredAccount_WritesTheKeywordThenTheMoveAndRecordsTheFiling()
    {
        // Arrange
        var recorder = this.Recorder(Filing(off: false));

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.Recorded, result.Outcome);
        Assert.True(result.IsMoved);
        Assert.Equal(
            [MailboxMutation.AddKeywords, MailboxMutation.Relocate],
            this.records.OpenedRequests.Select(static opened => opened.Mutation));
        Assert.Equal("Junk", this.records.OpenedRequests[1].DestinationPath?.Value);
        Assert.Contains(SpamVerdictAct.FiledIntoJunk, this.verdictActs.Of(Email));
        Assert.Equal(2, result.Records.Count);
    }

    /// <summary>With the switch on, not spam takes a message out of the junk folder to the inbox, and records no filing.</summary>
    [Fact]
    public async Task RecordAsync_NotSpamWithFilingOnAndTheMessageInJunk_MovesItToTheInbox()
    {
        // Arrange
        var recorder = this.Recorder(Filing(off: false), folder: Junk, keywords: ["$Junk"]);

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.NotSpam, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsMoved);
        var move = Assert.Single(this.records.OpenedRequests, static opened => opened.Mutation == MailboxMutation.Relocate);
        Assert.Equal("INBOX", move.DestinationPath?.Value);
        Assert.DoesNotContain(SpamVerdictAct.FiledIntoJunk, this.verdictActs.Of(Email));
    }

    /// <summary>A message already where the mark would put it stays there: not spam outside junk, and spam already in junk.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordAsync_TheMessageAlreadyWhereTheMarkWouldPutIt_MovesNothing(bool marksSpam)
    {
        // Arrange
        var recorder = this.Recorder(Filing(off: false), folder: marksSpam ? Junk : Inbox);

        // Act
        var result = await recorder.RecordAsync(
            Email,
            marksSpam ? SpamMark.Spam(subcategory: null) : SpamMark.NotSpam,
            Requester,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsMoved);
        Assert.DoesNotContain(this.records.OpenedRequests, static opened => opened.Mutation == MailboxMutation.Relocate);
    }

    /// <summary>With the switch off, neither mark moves anything, whichever folder the message is in.</summary>
    [Fact]
    public async Task RecordAsync_NotSpamWithFilingOffAndTheMessageInJunk_LeavesItThere()
    {
        // Arrange
        var recorder = this.Recorder(Filing(off: true), folder: Junk, keywords: ["$Junk"]);

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.NotSpam, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsMoved);
        Assert.DoesNotContain(this.records.OpenedRequests, static opened => opened.Mutation == MailboxMutation.Relocate);
    }

    /// <summary>On a held account every change commits to stored state at once, the move into the local junk folder included.</summary>
    [Fact]
    public async Task RecordAsync_SpamWithFilingOnAHeldAccount_CommitsTheKeywordAndTheMoveLocallyAndRecordsTheFiling()
    {
        // Arrange
        var localFolders = LocalFoldersOf(MailAccountCustodyPhase.Held);
        var states = StatesOfAMessageIn(InboxFolder);
        var recorder = this.Recorder(Filing(off: false), localFolders: localFolders, states: states);

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.Applied, result.Outcome);
        Assert.True(result.IsMoved);
        Assert.Equal(0, this.records.OpenedRecordCount);
        Assert.Equal(JunkFolder, states.States[Email].Folder);
        Assert.Equal(["$JUNK"], states.States[Email].Keywords.Values);
        Assert.Contains(SpamVerdictAct.FiledIntoJunk, this.verdictActs.Of(Email));
    }

    /// <summary>A held message the person marks not spam in the local junk folder goes back to the local inbox with <c>$NotJunk</c>.</summary>
    [Fact]
    public async Task RecordAsync_NotSpamWithFilingOnAHeldAccountInJunk_CommitsTheMoveToTheInboxLocally()
    {
        // Arrange
        var localFolders = LocalFoldersOf(MailAccountCustodyPhase.Held);
        var states = StatesOfAMessageIn(JunkFolder, "$Junk");
        var recorder = this.Recorder(
            Filing(off: false),
            folder: Junk,
            keywords: ["$Junk"],
            localFolders: localFolders,
            states: states);

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.NotSpam, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.Applied, result.Outcome);
        Assert.Equal(InboxFolder, states.States[Email].Folder);
        Assert.Equal(["$NOTJUNK"], states.States[Email].Keywords.Values);
    }

    /// <summary>A restoring account commits locally and opens the records its source is still owed, and the answer names them.</summary>
    [Fact]
    public async Task RecordAsync_SpamOnARestoringAccount_CommitsTheKeywordAndNamesTheRecordTheSourceIsOwed()
    {
        // Arrange
        var states = StatesOfAMessageIn(InboxFolder);
        var recorder = this.Recorder(
            Filing(off: true),
            localFolders: LocalFoldersOf(MailAccountCustodyPhase.Restoring),
            states: states);

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.Applied, result.Outcome);
        Assert.Equal(MailboxMutation.AddKeywords, Assert.Single(result.Records).Request.Mutation);
        Assert.Equal(["$JUNK"], states.States[Email].Keywords.Values);
    }

    /// <summary>A message this deployment serves nobody under that identity records nothing, so asking says nothing about what exists.</summary>
    [Fact]
    public async Task RecordAsync_AMessageNoReadableTargetNames_RecordsNothing()
    {
        // Arrange
        var recorder = this.Recorder(Filing(off: true), target: false);

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.NotSpam, Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.MessageNotFound, result.Outcome);
        Assert.Empty(this.classifications.Saved);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A message in a folder withheld from the person is one they may not learn about, so marking it records nothing.</summary>
    [Fact]
    public async Task RecordAsync_AMessageInAFolderWithheldFromThePerson_ReportsItAsNotFoundAndRecordsNothing()
    {
        // Arrange
        var recorder = this.Recorder(
            Filing(off: true),
            participation: StubMailFolderParticipation.Mapping(new MailFolderIdentity(Account, Junk)));

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamMarkOutcome.MessageNotFound, result.Outcome);
        Assert.Empty(this.classifications.Saved);
        Assert.Empty(this.verdictActs.Of(Email));
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A junk folder the person may not read is no destination for them, so the verdict is recorded and the message stays.</summary>
    [Fact]
    public async Task RecordAsync_SpamWithFilingOnWhereTheJunkFolderIsWithheldFromThePerson_RecordsTheVerdictWithoutAMove()
    {
        // Arrange
        var recorder = this.Recorder(
            Filing(off: false),
            participation: StubMailFolderParticipation.Mapping(new MailFolderIdentity(Account, Inbox)));

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsMoved);
        Assert.Equal(MailboxMutation.AddKeywords, Assert.Single(this.records.OpenedRequests).Mutation);
        Assert.Equal(SpamClassificationStage.Person, Assert.Single(this.classifications.Saved).DecidedBy);
    }

    /// <summary>The sent folder keeps its own mail, so a mark on a message there records the verdict and never files it into junk.</summary>
    [Fact]
    public async Task RecordAsync_SpamWithFilingOnForAMessageInTheSentFolder_RecordsTheVerdictWithoutAMove()
    {
        // Arrange
        var recorder = this.Recorder(Filing(off: false), folder: Sent);

        // Act
        var result = await recorder.RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsMoved);
        Assert.Equal(MailboxMutation.AddKeywords, Assert.Single(this.records.OpenedRequests).Mutation);
        Assert.DoesNotContain(SpamVerdictAct.FiledIntoJunk, this.verdictActs.Of(Email));
    }

    /// <summary>
    /// Every grant a batch needs is asked for before anything of it is written, so a caller without the moving grant on an
    /// account whose switch files junk is refused as a whole rather than after part of the batch committed.
    /// </summary>
    [Fact]
    public async Task RequireGrantsAsync_ACallerHoldingOnlyTheFlagGrantOnAnAccountThatFilesJunk_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var recorder = this.Recorder(
            Filing(off: false),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            recorder.RequireGrantsAsync([Email], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailMove, refusal.RequiredPermission);
        Assert.Empty(this.classifications.Saved);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>An account whose switch files nothing needs no moving grant, so the flag grant alone admits the batch.</summary>
    [Fact]
    public async Task RequireGrantsAsync_ACallerHoldingOnlyTheFlagGrantOnAnAccountThatFilesNothing_IsAdmitted()
    {
        // Arrange
        var recorder = this.Recorder(
            Filing(off: true),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite));

        // Act
        var admitted = await Record.ExceptionAsync(() => recorder.RequireGrantsAsync([Email], TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(admitted);
    }

    /// <summary>A mark that moves mail asks for the moving grant on top of the flag grant, because the move puts mail somewhere else.</summary>
    [Fact]
    public async Task RecordAsync_ACallerHoldingOnlyTheFlagGrantWhereTheMarkMoves_IsRefusedWithoutRecordingAnything()
    {
        // Arrange
        var recorder = this.Recorder(
            Filing(off: false),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            recorder.RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailMove, refusal.RequiredPermission);
        Assert.Empty(this.classifications.Saved);
    }

    /// <summary>
    /// The person filed it and somebody moved it back out: the operator's switches read the filing the mark recorded and
    /// leave the message where it was moved, still spam — on a mirrored account and on a held one alike.
    /// </summary>
    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored)]
    [InlineData(MailAccountCustodyPhase.Held)]
    public async Task RecordAsync_AMessageThePersonFiledAndSomebodyMovedBackOut_IsNeverFiledAgainByAVerdict(MailAccountCustodyPhase phase)
    {
        // Arrange
        var localFolders = phase is MailAccountCustodyPhase.Held ? LocalFoldersOf(phase) : null;
        var states = StatesOfAMessageIn(InboxFolder);
        await this.Recorder(Filing(off: false), localFolders: localFolders, states: states)
            .RecordAsync(Email, SpamMark.Spam(subcategory: null), Requester, TestContext.Current.CancellationToken);
        states.Store(Email, states.States[Email] with { Folder = InboxFolder });
        var actions = this.ActionRecorder(localFolders, states);

        // Act
        var acted = await actions.RecordAsync(
            await this.classifications.FindAsync(Email, TestContext.Current.CancellationToken) ?? throw new InvalidOperationException(),
            SpamActionPosture.Acting,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.PreviouslyFiled, acted.Outcome);
    }

    /// <summary>A message the person marked not spam is never filed back into junk by a verdict, whatever moves it afterwards.</summary>
    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored)]
    [InlineData(MailAccountCustodyPhase.Held)]
    public async Task RecordAsync_AMessageThePersonMarkedNotSpam_IsNeverFiledBackByAVerdict(MailAccountCustodyPhase phase)
    {
        // Arrange
        var localFolders = phase is MailAccountCustodyPhase.Held ? LocalFoldersOf(phase) : null;
        var states = StatesOfAMessageIn(JunkFolder, "$Junk");
        await this.Recorder(Filing(off: false), folder: Junk, keywords: ["$Junk"], localFolders: localFolders, states: states)
            .RecordAsync(Email, SpamMark.NotSpam, Requester, TestContext.Current.CancellationToken);
        var opened = this.records.OpenedRecordCount;
        var actions = this.ActionRecorder(localFolders, states);

        // Act
        var acted = await actions.RecordAsync(
            await this.classifications.FindAsync(Email, TestContext.Current.CancellationToken) ?? throw new InvalidOperationException(),
            SpamActionPosture.Acting,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamActionOutcome.NotSpam, acted.Outcome);
        Assert.Equal(opened, this.records.OpenedRecordCount);
    }

    private static SpamActionSettings Filing(bool off) =>
        SpamActionSettings.Create(filesJunk: !off, marksJunkRead: true, marksJunkWithKeywords: true);

    private static SpamClassification PersonsVerdict(SpamVerdict verdict, SpamSubcategory? subcategory) =>
        SpamClassification.Create(
            Email,
            verdict,
            SpamClassificationStage.Person,
            assessment: null,
            profile: default,
            signals: [],
            MarkedAt.AddDays(-1),
            subcategory);

    private static InMemoryLocalMailFolderStore LocalFoldersOf(MailAccountCustodyPhase phase)
    {
        var folders = new InMemoryLocalMailFolderStore(Account, phase);
        folders.Add(new LocalMailFolder(InboxFolder, ParentId: null, LocalMailFolderName.Create("Inbox"), MailFolderSpecialUse.Inbox, Inbox));
        folders.Add(new LocalMailFolder(JunkFolder, ParentId: null, LocalMailFolderName.Create("Junk"), MailFolderSpecialUse.Junk, Junk));

        return folders;
    }

    private static InMemoryLocalEmailStateStore StatesOfAMessageIn(MailFolderId folder, params string[] keywords)
    {
        var states = new InMemoryLocalEmailStateStore(Account);
        states.Store(
            Email,
            new LocalEmailState(
                MailFolderResolution.FirstBindingOf(Inbox, RemoteFolderPath.Create("INBOX")),
                HoldsSourceOccurrence: true,
                folder,
                IsSeen: false,
                IsFlagged: false,
                RemoteEmailKeywords.Create(keywords)));

        return states;
    }

    private static AuthoredMailboxTarget TargetIn(MailFolderAlias folderAlias)
    {
        var (remotePath, folder) = folderAlias == Junk ? ("Junk", JunkFolder)
            : folderAlias == Sent ? ("Sent", SentFolder)
            : ("INBOX", InboxFolder);
        var binding = MailFolderResolution.FirstBindingOf(folderAlias, RemoteFolderPath.Create(remotePath));

        return new AuthoredMailboxTarget(
            EmailOccurrenceId.Create(Account, binding.Id, ImapUidValidity.Create(9), ImapUid.Create(4401)),
            binding,
            folder);
    }

    private MailboxCustodianComposition Composition(
        InMemoryLocalMailFolderStore? localFolders,
        InMemoryLocalEmailStateStore? states)
    {
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var transportPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
        transportPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(TlsOnConnect);

        var folders = localFolders ?? LocalFoldersOf(MailAccountCustodyPhase.Mirrored);

        return new MailboxCustodianComposition(new FakeTimeProvider(MarkedAt))
        {
            Records = this.records,
            LocalFolders = folders,
            Custody = folders.Custody,
            Copier = LocalMailCopiers.Over(folders),
            States = states ?? new InMemoryLocalEmailStateStore(Account),
            Mappings = this.mappings,
            FolderResolutions = this.bindings,
            RemoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>(),
            RemoteFolderCreator = Substitute.For<IRemoteFolderCreator>(),
            MappingChangeAuditor = Substitute.For<IMailFolderMappingChangeAuditor>(),
            PersistenceSessions = sessions,
            TransportPolicies = transportPolicies,
        };
    }

    private SpamMarkRecorder Recorder(
        SpamActionSettings settings,
        MailFolderAlias? folder = null,
        string[]? keywords = null,
        InMemoryLocalMailFolderStore? localFolders = null,
        InMemoryLocalEmailStateStore? states = null,
        MailRuleActionPermissions? permissions = null,
        AccessAuthorization? authorization = null,
        bool target = true,
        StubMailFolderParticipation? participation = null)
    {
        var folderAlias = folder ?? Inbox;
        var composition = this.Composition(localFolders, states);
        var caller = authorization
            ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite, MailFathomPermission.MailMove);

        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(target ? TargetIn(folderAlias) : null);

        var settingsReader = Substitute.For<ISpamActionSettingsReader>();
        settingsReader.ActionsFor(Account).Returns(settings);

        var readable = participation ?? StubMailFolderParticipation.Mapping(
            new MailFolderIdentity(Account, Inbox),
            new MailFolderIdentity(Account, Junk),
            new MailFolderIdentity(Account, Sent));

        return new SpamMarkRecorder(
            caller,
            new MailboxScopeResolver(
                AssignedMailAccountCatalogs.For(caller, SyntheticServedAccount.Of(Account)),
                readable,
                StubDeploymentMailFolders.Of(readable),
                StubMailFolderMappings.ResolvingNothing),
            targets,
            settingsReader,
            StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Account, Junk)),
            composition.LocalFolders,
            composition.Destinations,
            this.dispositions,
            this.Writer(composition, folderAlias, keywords ?? [], permissions ?? MailRuleActionPermissions.Default),
            composition.Submission,
            CommitPolicyOver(composition),
            new MailAccountRunSignal(),
            this.mappings.Resolver,
            this.bindings);
    }

    private static OptimisticConcurrencyRetryPolicy CommitPolicyOver(MailboxCustodianComposition composition) =>
        new(composition.PersistenceSessions, new PersistenceConcurrencyOptions(), composition.Clock);

    private SpamMarkWriter Writer(
        MailboxCustodianComposition composition,
        MailFolderAlias folder,
        string[] keywords,
        MailRuleActionPermissions permissions)
    {
        var permissionReader = Substitute.For<IMailRuleActionPermissionReader>();
        permissionReader.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(permissions);

        return new SpamMarkWriter(
            SpamClassificationHarness.OccurrenceReader(Account, folder, keywords),
            permissionReader,
            this.classifications,
            this.verdictActs,
            this.chunks,
            Substitute.For<IStoredEmailAttachmentTextStore>(),
            Substitute.For<IDerivedWorkGateTelemetry>(),
            composition.Submission,
            composition.Clock);
    }

    /// <summary>Builds what an operator's switches do with a verdict, over the same records and acts the mark wrote, as a later classification would.</summary>
    private SpamActionRecorder ActionRecorder(
        InMemoryLocalMailFolderStore? localFolders,
        InMemoryLocalEmailStateStore states)
    {
        var composition = this.Composition(localFolders, states);
        var settingsReader = Substitute.For<ISpamActionSettingsReader>();
        settingsReader.ActionsFor(Account).Returns(Filing(off: false));
        var permissionReader = Substitute.For<IMailRuleActionPermissionReader>();
        permissionReader.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(MailRuleActionPermissions.Default);

        return new SpamActionRecorder(
            settingsReader,
            SpamClassificationHarness.OccurrenceReader(Account, Inbox),
            this.records,
            this.verdictActs,
            composition.Submission,
            composition.Destinations,
            this.dispositions,
            permissionReader,
            CommitPolicyOver(composition));
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
