// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
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
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Authoring;

/// <summary>Covers the moving grant, the visibility rule the destination is judged by, and the record a move is written down as.</summary>
/// <remarks>
/// Every refusal here is a result rather than an exception, because a caller moving several messages at once reads one
/// answer per message and carries on. The one exception is the grant, which is the whole request's outcome.
/// </remarks>
public sealed class MailRelocationRecorderTests
{
    private static readonly MailAccountId Account =
        MailAccountId.Create("personal");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("Archive");

    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("Private");

    private static readonly MailFolderAlias Drafts = MailFolderAlias.Create("Drafts");

    private static readonly MailFolderAlias Sent = MailFolderAlias.Create("Sent");

    private static readonly MailFolderAlias DraftsClients = MailFolderAlias.Create("DraftsClients");

    private static readonly MailFolderAlias SentClients = MailFolderAlias.Create("SentClients");

    private static readonly MailFolderAlias Trash = MailFolderAlias.Create("Trash");

    private static readonly MailFolderAlias Projects = MailFolderAlias.Create("Projects");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("Junk");

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailTransportSecurityPolicy TlsOnConnect = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private readonly InMemoryMailboxMutationRecordStore records = new();

    private readonly InMemoryMailFolderResolutionStore bindings = new();

    private readonly StubMailFolderMappings mappings = StubMailFolderMappings.Nothing;

    private readonly InMemoryEmailSpamClassificationStore classifications = new();

    private readonly InMemorySpamVerdictActStore verdictActs = new();

    private readonly List<RemoteFolder> advertisedFolders = [];

    private readonly List<LocalMailFolder> extraFolders = [];

    private readonly IAuthoredDeleteEmailDispositionReader dispositions =
        Substitute.For<IAuthoredDeleteEmailDispositionReader>();

    /// <summary>Arranges the disposition every test shares, so a test that is about it overrides it afterwards.</summary>
    public MailRelocationRecorderTests() => this.dispositions
        .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
        .Returns(AuthoredDeleteEmailDisposition.RetainLocalCopy);

    /// <summary>The record names the occurrence and the remote path a command will be issued against, neither of which the caller supplied.</summary>
    [Fact]
    public async Task RecordAsync_AMoveIntoAMirroredFolder_OpensOneRecordAgainstTheOccurrence()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive/2026");
        var target = TargetIn(Inbox);
        var recorder = this.Recorder(target);

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Archive),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.Recorded, result.Outcome);
        Assert.Equal(Archive, result.Destination);
        Assert.Equal(MailboxMutationLifecycle.Pending, result.Lifecycle);

        var request = Assert.Single(this.records.OpenedRequests);

        Assert.Equal(MailboxMutation.Relocate, request.Mutation);
        Assert.Equal(target.Occurrence, request.Occurrence);
        Assert.Equal(RemoteFolderPath.Create("Archive/2026", '/'), request.DestinationPath);
    }

    /// <summary>A mirrored destination keeps the local copy the mirror already carries, so the record names no disposition.</summary>
    [Fact]
    public async Task RecordAsync_AMoveIntoAMirroredFolder_NamesNoLocalDisposition()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        await recorder.RecordAsync(LocalEmail, IdOf(Archive), Requester, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(this.records.OpenedRequests).LocalDisposition);
        this.dispositions.DidNotReceive().GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>());
    }

    /// <summary>
    /// Mail moved into a folder nothing mirrors leaves the mirror for good, so what becomes of the local copy is the
    /// account's own answer and is written onto the record while the user's configuration still says it.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AMoveIntoAnUnmirroredFolder_RecordsWhatBecomesOfTheLocalCopy()
    {
        // Arrange
        this.MapUnmirrored(Archive, "Archive");
        this.dispositions
            .GetAuthoredDeleteDisposition(Account)
            .Returns(AuthoredDeleteEmailDisposition.EraseLocalCopy);
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Archive),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.Recorded, result.Outcome);
        Assert.Equal(
            AuthoredDeleteEmailDisposition.EraseLocalCopy,
            Assert.Single(this.records.OpenedRequests).LocalDisposition);
    }

    /// <summary>
    /// Moving mail is its own grant, so a caller holding the one that writes flags is refused. A flag misdescribes mail
    /// the user can still find; a move puts the mail somewhere else.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ACallerHoldingOnlyTheFlagGrant_IsRefusedWithoutWritingAnything()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(
            TargetIn(Inbox),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            recorder.RecordAsync(LocalEmail, IdOf(Archive), Requester, MailboxReaderKind.Agent, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailMove, refusal.RequiredPermission);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A folder no agent may read is a folder no agent may move mail out of, or the write surface would be the way round the withholding.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailInAFolderWithheldFromAIAskedByAnAgent_ReportsTheMessageAsNotFound()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(TargetIn(Withheld));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Archive),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.MessageNotFound, result.Outcome);
        Assert.Null(result.RecordId);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A folder withheld from AI is still the person's, so moving its mail through the client is recorded like any other move.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailInAFolderWithheldFromAIAskedByThePerson_OpensTheRecord()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(TargetIn(Withheld));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Archive),
            Requester,
            MailboxReaderKind.Person,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.RecordId);
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    /// <summary>An email no row carries answers exactly as a withheld one does, so asking cannot reveal which identifiers exist.</summary>
    [Fact]
    public async Task RecordAsync_AnEmailThisDeploymentHoldsNoRowFor_ReportsTheMessageAsNotFound()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(target: null);

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Archive),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.MessageNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// A destination withheld from AI is refused to an agent as a destination that is not there, because filing mail
    /// into a folder the caller cannot read would move it out of sight rather than be a capability of its own.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ADestinationWithheldFromAIAskedByAnAgent_ReportsTheDestinationAsNotFound()
    {
        // Arrange
        this.MapMirrored(Withheld, "Private");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Withheld),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.DestinationNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>The person may file their mail into a folder withheld from AI, since it is a folder they read.</summary>
    [Fact]
    public async Task RecordAsync_ADestinationWithheldFromAIAskedByThePerson_OpensTheRecord()
    {
        // Arrange
        this.MapMirrored(Withheld, "Private");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Withheld),
            Requester,
            MailboxReaderKind.Person,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.RecordId);
        Assert.Equal(1, this.records.OpenedRecordCount);
    }

    /// <summary>A name this deployment maps no folder to reaches the same refusal as one it withholds, and writes nothing either way.</summary>
    [Fact]
    public async Task RecordAsync_ADestinationNoMappingNames_ReportsTheDestinationAsNotFound()
    {
        // Arrange
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Archive),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.DestinationNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A move to where the message already is is nothing to carry out, and a record would be a command the server refuses.</summary>
    [Fact]
    public async Task RecordAsync_ADestinationTheEmailIsAlreadyIn_ReportsItAsAlreadyThere()
    {
        // Arrange
        this.MapMirrored(Inbox, "INBOX");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Inbox),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.AlreadyInDestination, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// An account removed from configuration between the read and the write leaves the disposition unanswerable, which
    /// is this caller's message to report rather than a fault that ends the batch.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AnAccountThatLeftConfigurationMidRequest_ReportsItRatherThanFailing()
    {
        // Arrange
        this.MapUnmirrored(Archive, "Archive");
        this.dispositions
            .GetAuthoredDeleteDisposition(Account)
            .Throws(new InvalidOperationException("No account carries the identifier personal."));
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(Archive),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.AccountNoLongerConfigured, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A retry under one identity is one move, because the record store admits one per occurrence, requester, and mutation.</summary>
    [Fact]
    public async Task RecordAsync_TheSameMoveAskedTwiceUnderOneRequester_OpensOneRecord()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var first = await recorder.RecordAsync(LocalEmail, IdOf(Archive), Requester, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);
        var second = await recorder.RecordAsync(LocalEmail, IdOf(Archive), Requester, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.records.OpenedRecordCount);
        Assert.Equal(first.RecordId, second.RecordId);
    }

    /// <summary>An identity no folder of the message's account carries is a destination that is not there, and writes nothing.</summary>
    [Fact]
    public async Task RecordAsync_AnIdentityNoFolderOfTheAccountCarries_ReportsTheDestinationAsNotFound()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000c9")),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.DestinationNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>A folder no alias feeds and no role names is not yet a destination, so a move into one writes nothing.</summary>
    [Fact]
    public async Task RecordAsync_AFolderNeitherAnAliasNorARoleNames_ReportsTheDestinationAsNotFound()
    {
        // Arrange
        var made = new LocalMailFolder(
            MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000ca")),
            ParentId: null,
            LocalMailFolderName.Create("Made by hand"),
            Role: null,
            SourceFolderAlias: null);
        this.extraFolders.Add(made);
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(LocalEmail, made.Id, Requester, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.DestinationNotFound, result.Outcome);
        Assert.Equal(0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// The drafts folder and the sent folder each stand for their whole subtree: mail moves freely inside one, nothing is
    /// filed into either from outside it, and what is in either leaves only for the trash, which is how it is deleted.
    /// Each pair here is a message's folder and a destination, with the answer the service gives the move.
    /// </summary>
    [Theory]
    [InlineData("INBOX", "Drafts", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("INBOX", "DraftsClients", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("INBOX", "Sent", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("Projects", "SentClients", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("Trash", "Drafts", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("Trash", "Sent", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("Drafts", "Sent", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("DraftsClients", "SentClients", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("Drafts", "Archive", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("DraftsClients", "Projects", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("Sent", "Archive", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("SentClients", "INBOX", MailRelocationOutcome.DestinationNotAllowed)]
    [InlineData("Drafts", "DraftsClients", MailRelocationOutcome.Recorded)]
    [InlineData("DraftsClients", "Drafts", MailRelocationOutcome.Recorded)]
    [InlineData("Sent", "SentClients", MailRelocationOutcome.Recorded)]
    [InlineData("SentClients", "Sent", MailRelocationOutcome.Recorded)]
    [InlineData("DraftsClients", "Trash", MailRelocationOutcome.Recorded)]
    [InlineData("Sent", "Trash", MailRelocationOutcome.Recorded)]
    [InlineData("INBOX", "Archive", MailRelocationOutcome.Recorded)]
    [InlineData("INBOX", "Projects", MailRelocationOutcome.Recorded)]
    public async Task RecordAsync_AMoveAcrossTheEdgeOfTheDraftsOrTheSentTree_IsAdmittedOnlyIntoTheTrash(
        string from,
        string into,
        MailRelocationOutcome expected)
    {
        // Arrange
        Dictionary<string, MailFolderResolution> bound = new()
        {
            ["INBOX"] = this.MapMirrored(Inbox, "INBOX", MailFolderSpecialUse.Inbox),
            ["Drafts"] = this.MapMirrored(Drafts, "Drafts", MailFolderSpecialUse.Drafts),
            ["DraftsClients"] = this.MapMirrored(DraftsClients, "Drafts/Clients"),
            ["Sent"] = this.MapMirrored(Sent, "Sent", MailFolderSpecialUse.Sent),
            ["SentClients"] = this.MapMirrored(SentClients, "Sent/Clients"),
            ["Archive"] = this.MapMirrored(Archive, "Archive", MailFolderSpecialUse.Archive),
            ["Trash"] = this.MapMirrored(Trash, "Trash", MailFolderSpecialUse.Trash),
            ["Projects"] = this.MapMirrored(Projects, "Projects"),
        };
        var source = bound[from];
        var recorder = this.Recorder(TargetIn(source.Alias, source.RemotePath.Value));

        // Act
        var result = await recorder.RecordAsync(
            LocalEmail,
            IdOf(bound[into].Alias),
            Requester,
            MailboxReaderKind.Agent,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expected == MailRelocationOutcome.Recorded ? 1 : 0, this.records.OpenedRecordCount);
    }

    /// <summary>
    /// Where the two folders are is read from what was recorded, so a move between two other folders asks the mail
    /// server nothing about a drafts folder nobody has found yet — and that folder still keeps its own mail as itself.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AnAccountWhoseDraftsFolderWasNeverFound_AsksTheServerNothingAboutIt()
    {
        // Arrange
        this.MapMirrored(Inbox, "INBOX", MailFolderSpecialUse.Inbox);
        this.MapMirrored(Archive, "Archive", MailFolderSpecialUse.Archive);
        this.mappings.With(
            Account,
            MailFolderMapping.ToRemotePath(
                Drafts,
                RemoteFolderPath.Create("Drafts"),
                MailFolderParticipation.MappedOnly,
                specialUse: MailFolderSpecialUse.Drafts));
        this.advertisedFolders.Add(new RemoteFolder(RemoteFolderPath.Create("Drafts", '/'), []));
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var archived = await recorder.RecordAsync(LocalEmail, IdOf(Archive), Requester, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);
        var draftsAfterArchiving =
            await this.bindings.GetCurrentResolutionAsync(Account, Drafts, TestContext.Current.CancellationToken);
        var intoDrafts = await recorder.RecordAsync(LocalEmail, IdOf(Drafts), Requester, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.Recorded, archived.Outcome);
        Assert.Null(draftsAfterArchiving);
        Assert.Equal(MailRelocationOutcome.DestinationNotAllowed, intoDrafts.Outcome);
    }

    /// <summary>
    /// The person moving a message into the junk folder is marking it spam: the verdict is theirs, the junk keyword is
    /// written before the move, and the filing is recorded so no verdict files it again once it is moved back out.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ThePersonMovingAMessageIntoTheJunkFolder_MarksItSpamInTheSameAct()
    {
        // Arrange
        this.MapMirrored(Junk, "Junk");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        var result = await recorder.RecordAsync(LocalEmail, IdOf(Junk), Requester, MailboxReaderKind.Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRelocationOutcome.Recorded, result.Outcome);
        Assert.Equal(
            [MailboxMutation.AddKeywords, MailboxMutation.Relocate],
            this.records.OpenedRequests.Select(static opened => opened.Mutation));

        var verdict = Assert.Single(this.classifications.Saved);
        Assert.Equal(
            (SpamVerdict.Spam, SpamClassificationStage.Person, (SpamSubcategory?)null),
            (verdict.Verdict, verdict.DecidedBy, verdict.Subcategory));
        Assert.Equal([SpamVerdictAct.FiledIntoJunk, SpamVerdictAct.MarkedJunk], this.verdictActs.Of(LocalEmail).Order());
    }

    /// <summary>A person who already named the kind of spam keeps it when they file the message themselves, and its keyword follows.</summary>
    [Fact]
    public async Task RecordAsync_ThePersonFilingAMessageTheyAlreadyMarkedPhishing_KeepsTheSubcategory()
    {
        // Arrange
        this.MapMirrored(Junk, "Junk");
        this.classifications.Hold(SpamClassification.Create(
            LocalEmail,
            SpamVerdict.Spam,
            SpamClassificationStage.Person,
            assessment: null,
            SpamClassificationProfile.Create(),
            [],
            RecordedAt.AddDays(-1),
            SpamSubcategory.Phishing));
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        await recorder.RecordAsync(LocalEmail, IdOf(Junk), Requester, MailboxReaderKind.Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamSubcategory.Phishing, Assert.Single(this.classifications.Saved).Subcategory);
        Assert.Equal(["$Junk", "$Phishing"], this.records.OpenedRequests[0].Keywords?.Values);
    }

    /// <summary>An agent filing a message into the junk folder moves it and nothing more, because only the person's own act is a verdict.</summary>
    [Fact]
    public async Task RecordAsync_AnAgentMovingAMessageIntoTheJunkFolder_RecordsOnlyTheMove()
    {
        // Arrange
        this.MapMirrored(Junk, "Junk");
        var recorder = this.Recorder(TargetIn(Inbox));

        // Act
        await recorder.RecordAsync(LocalEmail, IdOf(Junk), Requester, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxMutation.Relocate, Assert.Single(this.records.OpenedRequests).Mutation);
        Assert.Empty(this.classifications.Saved);
        Assert.Empty(this.verdictActs.Of(LocalEmail));
    }

    /// <summary>Maps a folder the account mirrors, and records the binding its own synchronization run would have.</summary>
    private MailFolderResolution MapMirrored(MailFolderAlias alias, string remotePath, MailFolderSpecialUse? role = null)
    {
        this.mappings.With(
            Account,
            MailFolderMapping.ToRemotePath(alias, RemoteFolderPath.Create(remotePath), specialUse: role));

        return this.bindings.Bind(Account, alias, remotePath, hierarchyDelimiter: '/');
    }

    /// <summary>A move a person watched themselves ask for must not sit under <i>moving to trash</i> until the interval is out.</summary>
    [Fact]
    public async Task RecordAsync_AMove_BringsTheAccountsNextSynchronizationRunForward()
    {
        // Arrange
        this.MapMirrored(Archive, "Archive");
        var runSignal = new MailAccountRunSignal();
        var recorder = this.Recorder(TargetIn(Inbox), runSignal: runSignal);

        // Act
        await recorder.RecordAsync(LocalEmail, IdOf(Archive), Requester, MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        using var waiting = runSignal.Register(Account, TestContext.Current.CancellationToken);

        Assert.True(waiting.Token.IsCancellationRequested);
    }

    /// <summary>Maps a folder no run schedules, which is resolved against what the server advertises at the moment it is needed.</summary>
    private void MapUnmirrored(MailFolderAlias alias, string remotePath)
    {
        this.mappings.With(
            Account,
            MailFolderMapping.ToRemotePath(
                alias,
                RemoteFolderPath.Create(remotePath),
                MailFolderParticipation.MappedOnly));
        this.advertisedFolders.Add(new RemoteFolder(RemoteFolderPath.Create(remotePath), []));
    }

    private MailRelocationRecorder Recorder(
        AuthoredMailboxTarget? target,
        AccessAuthorization? authorization = null,
        MailAccountRunSignal? runSignal = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailMove);
        var accountCatalog = AssignedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Account));

        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var participation = StubMailFolderParticipation
            .Mapping(
                new MailFolderIdentity(Account, Inbox),
                new MailFolderIdentity(Account, Archive),
                new MailFolderIdentity(Account, Drafts),
                new MailFolderIdentity(Account, Sent),
                new MailFolderIdentity(Account, DraftsClients),
                new MailFolderIdentity(Account, SentClients),
                new MailFolderIdentity(Account, Trash),
                new MailFolderIdentity(Account, Projects),
                new MailFolderIdentity(Account, Junk))
            .Hiding(new MailFolderIdentity(Account, Withheld));

        var submission = MailboxChangeSubmissions.Over(this.records);
        var clock = new FakeTimeProvider(RecordedAt);

        return new MailRelocationRecorder(
            callerAuthorization,
            new MailboxScopeResolver(
                accountCatalog,
                participation,
                StubDeploymentMailFolders.Of(participation),
                StubMailFolderMappings.ResolvingNothing),
            targets,
            this.mappings.Resolver,
            this.bindings,
            this.FolderRows(),
            this.DestinationResolver(sessions),
            this.dispositions,
            submission,
            new OptimisticConcurrencyRetryPolicy(sessions, new PersistenceConcurrencyOptions(), clock),
            runSignal ?? new MailAccountRunSignal(),
            StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Account, Junk)),
            new SpamMarkWriter(
                SpamClassificationHarness.OccurrenceReader(Account, target?.Folder.Alias ?? Inbox),
                SpamClassificationHarness.KeywordsPermitted(),
                this.classifications,
                this.verdictActs,
                Substitute.For<IEmailChunkStore>(),
                Substitute.For<IStoredEmailAttachmentTextStore>(),
                Substitute.For<IDerivedWorkGateTelemetry>(),
                submission,
                clock));
    }

    /// <summary>Gives every folder these tests name a row, which is what a destination is named by.</summary>
    private ILocalMailFolderStore FolderRows()
    {
        MailFolderAlias[] named = [Inbox, Archive, Withheld, Drafts, Sent, DraftsClients, SentClients, Trash, Projects, Junk];
        IReadOnlyList<LocalMailFolder> rows =
        [
            .. named.Select(static alias => new LocalMailFolder(
                IdOf(alias),
                ParentId: null,
                LocalMailFolderName.Create(alias.Value),
                Role: null,
                alias)),
            .. this.extraFolders,
        ];

        var folders = Substitute.For<ILocalMailFolderStore>();
        folders.ReadFoldersAsync(Account, Arg.Any<CancellationToken>()).Returns(rows);

        return folders;
    }

    private static MailFolderId IdOf(MailFolderAlias alias) =>
        MailFolderId.Create(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(alias.Value)).AsSpan(0, 16)));

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

        return new MailboxCustodianComposition(new FakeTimeProvider(RecordedAt))
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

    private static AuthoredMailboxTarget TargetIn(MailFolderAlias folderAlias, string? remotePath = null)
    {
        var folder = MailFolderResolution.FirstBindingOf(
            folderAlias,
            RemoteFolderPath.Create(remotePath ?? folderAlias.Value));

        return new AuthoredMailboxTarget(
            EmailOccurrenceId.Create(Account, folder.Id, ImapUidValidity.Create(42), ImapUid.Create(7)),
            folder,
            IdOf(folderAlias));
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
