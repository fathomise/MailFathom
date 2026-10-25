// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam;

public sealed class EmailSpamClassifierTests
{
    private static readonly StoredEmailId Occurrence =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000abcd"));

    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private static readonly MailAccountId Archive = MailAccountId.Create("acct-2");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryEmailSpamClassificationStore store = new();

    private readonly IEmailContentStore contentStore = ContentStores.Substituted();

    private readonly IEmailChunkStore chunkStore = Substitute.For<IEmailChunkStore>();

    private readonly IStoredEmailAttachmentTextStore attachmentTextStore =
        Substitute.For<IStoredEmailAttachmentTextStore>();

    private readonly RecordingDerivedWorkGateTelemetry gateTelemetry = new();

    [Fact]
    public async Task ClassifyAsync_ClassificationSwitchedOff_RecordsNothingAndReadsNoMail()
    {
        // Arrange
        var classifier = this.Classifier(SpamClassificationSettings.Disabled, FactsSaying("X-Spam-Flag", "YES"));

        // Act
        var result = await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.Disabled, result.Outcome);
        Assert.Null(result.Classification);
        Assert.Empty(this.store.Saved);
        await this.contentStore
            .DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_AFolderTheScopeDoesNotCover_RecordsNothingAndReadsNoMail()
    {
        // Arrange
        var classifier = this.Classifier(SettingsCovering(Junk), FactsSaying("X-Spam-Flag", "YES"));

        // Act
        var result = await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.OutsideConfiguredScope, result.Outcome);
        Assert.Empty(this.store.Saved);
        await this.contentStore
            .DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_AnOccurrenceNothingIsStoredFor_ReportsItGoneRatherThanFailing()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox),
            FactsSaying("X-Spam-Flag", "YES"),
            email: null,
            withEmail: false);

        // Act
        var result = await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.OccurrenceMissing, result.Outcome);
        Assert.Empty(this.store.Saved);
    }

    /// <summary>Mail over the size limit is stored as metadata alone, and classifying it is refused rather than fetched.</summary>
    [Fact]
    public async Task ClassifyAsync_AnOccurrenceWithNoLocalContent_ReportsItRatherThanFetchingIt()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox),
            FactsSaying("X-Spam-Flag", "YES"),
            withContent: false);

        // Act
        var result = await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.ContentUnavailable, result.Outcome);
        Assert.Empty(this.store.Saved);
    }

    [Fact]
    public async Task ClassifyAsync_AProviderVerdictInTheHeaders_RecordsItAgainstTheOccurrence()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox),
            FactsSaying("X-Spam-Status", "Yes, score=15.2 required=5.0"));

        // Act
        var result = await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);

        Assert.Equal(SpamClassificationOutcome.Classified, result.Outcome);
        Assert.Equal(saved, result.Classification);
        Assert.Equal(Occurrence, saved.EmailId);
        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, saved.DecidedBy);
        Assert.Equal(EvaluatedAt, saved.EvaluatedAt);
    }

    /// <summary>
    /// Ordering keeps derived data from being created for junk in the first place, so this reaches only mail that was
    /// derived before anybody scored it — which is what an on-demand run over an existing mailbox meets. The attachment
    /// readings go with the passages: a reading is a second durable copy of the same words, and one left behind is a
    /// document's whole text still stored for a message the verdict called junk.
    /// </summary>
    [Fact]
    public async Task ClassifyAsync_AVerdictOfSpam_RemovesEverythingAlreadyDerivedFromTheMessage()
    {
        // Arrange
        this.chunkStore
            .DiscardChunksAsync(Arg.Any<IPersistenceSession>(), Occurrence, Arg.Any<CancellationToken>())
            .Returns(4);
        var classifier = this.Classifier(
            SettingsCovering(Inbox),
            FactsSaying("X-Spam-Status", "Yes, score=15.2 required=5.0"));

        // Act
        await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        await this.chunkStore
            .Received(1)
            .DiscardChunksAsync(Arg.Any<IPersistenceSession>(), Occurrence, Arg.Any<CancellationToken>());
        await this.attachmentTextStore
            .Received(1)
            .DiscardAttachmentTextAsync(Arg.Any<IPersistenceSession>(), Occurrence, Arg.Any<CancellationToken>());
        Assert.Equal([4], this.gateTelemetry.DiscardedPassageCounts);
    }

    /// <summary>Ordinary correspondence keeps everything derived from it, whichever way the verdict fell short of spam.</summary>
    [Theory]
    [InlineData("X-Spam-Status", "No, score=0.1 required=5.0")]
    [InlineData("X-Nothing-Relevant", "")]
    public async Task ClassifyAsync_AVerdictThatIsNotSpam_LeavesThePassagesAndVectorsAlone(string field, string value)
    {
        // Arrange
        var classifier = this.Classifier(SettingsCovering(Inbox), FactsSaying(field, value));

        // Act
        await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(SpamVerdict.Spam, Assert.Single(this.store.Saved).Verdict);
        await this.chunkStore.DidNotReceiveWithAnyArgs().DiscardChunksAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<StoredEmailId>(),
            Arg.Any<CancellationToken>());
        await this.attachmentTextStore.DidNotReceiveWithAnyArgs().DiscardAttachmentTextAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<StoredEmailId>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(this.gateTelemetry.DiscardedPassageCounts);
    }

    /// <summary>An arrival trigger fires per message, so a repeat has to leave an existing record alone.</summary>
    [Fact]
    public async Task ClassifyAsync_AnOccurrenceAlreadyClassified_LeavesTheRecordAlone()
    {
        // Arrange
        this.store.Hold(Recorded(SpamVerdict.NotSpam));

        var classifier = this.Classifier(SettingsCovering(Inbox), FactsSaying("X-Spam-Flag", "YES"));

        // Act
        var result = await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.AlreadyClassified, result.Outcome);
        Assert.Empty(this.store.Saved);
    }

    /// <summary>Replacing a verdict is what an operator asks for explicitly, and nothing else sets it off.</summary>
    [Fact]
    public async Task ClassifyAsync_AReclassificationOfAClassifiedOccurrence_ReplacesTheRecord()
    {
        // Arrange
        this.store.Hold(Recorded(SpamVerdict.NotSpam));

        var classifier = this.Classifier(SettingsCovering(Inbox), FactsSaying("X-Spam-Flag", "YES"));

        // Act
        var result = await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.Reclassify,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);

        Assert.Equal(SpamClassificationOutcome.Classified, result.Outcome);
        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
    }

    /// <summary>The person's verdict outranks every stage, so even an explicit reclassification leaves it where it is.</summary>
    [Theory]
    [InlineData(SpamClassificationMode.Reclassify)]
    [InlineData(SpamClassificationMode.FirstTimeOnly)]
    public async Task ClassifyAsync_TheOccurrenceCarriesThePersonsVerdict_KeepsItAndRecordsNothing(SpamClassificationMode mode)
    {
        // Arrange
        var personsVerdict = SpamClassification.Create(
            Occurrence,
            SpamVerdict.NotSpam,
            SpamClassificationStage.Person,
            assessment: null,
            SpamClassificationProfile.Create(),
            [],
            EvaluatedAt.AddDays(-1));

        this.store.Hold(personsVerdict);

        var classifier = this.Classifier(SettingsCovering(Inbox), FactsSaying("X-Spam-Flag", "YES"));

        // Act
        var result = await classifier.ClassifyAsync(
            Account,
            Occurrence,
            mode,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.PersonsVerdictKept, result.Outcome);
        Assert.Same(personsVerdict, result.Classification);
        Assert.Empty(this.store.Saved);
        await this.contentStore.DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_AnOccurrenceInTheJunkFolder_RecordsThePlacementAsTheReason()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Junk),
            SpamHeaderFacts.None,
            email: new ClassifiableEmail(Occurrence, Account, Junk),
            junkFolders: StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Account, Junk)));

        // Act
        var result = await classifier.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);
        var signal = Assert.Single(saved.Signals);

        Assert.Equal(SpamClassificationOutcome.Classified, result.Outcome);
        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
        Assert.Equal(SpamSignalKind.JunkFolderPlacement, signal.Kind);
    }

    [Fact]
    public async Task ClassifyAsync_AModeOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange
        var classifier = this.Classifier(SettingsCovering(Inbox), SpamHeaderFacts.None);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => classifier.ClassifyAsync(
            Account,
            Occurrence,
            (SpamClassificationMode)7,
            TestContext.Current.CancellationToken));
    }

    private static SpamClassificationSettings SettingsCovering(MailFolderAlias alias) =>
        SpamClassificationSettings.Create(isEnabled: true, [alias]);

    /// <summary>
    /// Two mailboxes of one user, one of them classified and one not. The settings are read for the mailbox the
    /// message is in, so the archive is left alone while the other mailbox is classified — a classifier reading the
    /// user's settings instead would answer for whichever of their mailboxes it resolved first, and file a message
    /// under a switch its own mailbox never had on.
    /// </summary>
    [Fact]
    public async Task ClassifyAsync_TwoMailboxesClassifiedDifferently_FollowsTheSettingsOfTheOneTheMessageIsIn()
    {
        // Arrange
        var flagged = FactsSaying("X-Spam-Flag", "YES");

        var inTheArchive = this.Classifier(
            SettingsCovering(Inbox),
            flagged,
            new ClassifiableEmail(Occurrence, Archive, Inbox),
            classifying: Account);

        var inTheClassifiedMailbox = this.Classifier(
            SettingsCovering(Inbox),
            flagged,
            new ClassifiableEmail(Occurrence, Account, Inbox),
            classifying: Account);

        // Act
        var archived = await inTheArchive.ClassifyAsync(
            Archive,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        var classified = await inTheClassifiedMailbox.ClassifyAsync(
            Account,
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.Disabled, archived.Outcome);
        Assert.Equal(SpamClassificationOutcome.Classified, classified.Outcome);
        Assert.Equal(SpamVerdict.Spam, Assert.Single(this.store.Saved).Verdict);
    }

    private static SpamHeaderFacts FactsSaying(string fieldName, string value) =>
        SpamHeaderFacts.Create([], [new ProviderSpamHeaderValue(fieldName, value)]);

    /// <summary>Builds content the classifier only hands to its collaborators, so the bytes themselves say nothing.</summary>
    private static StoredEmailContent SomeContent()
    {
        var rawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nA body nothing here reads.\r\n");

        return new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime));
    }

    private static SpamClassification Recorded(SpamVerdict verdict) => SpamClassification.Create(
        Occurrence,
        verdict,
        SpamClassificationStage.Deterministic,
        assessment: null,
        SpamClassificationProfile.Create(),
        [],
        EvaluatedAt.AddDays(-1));

    /// <summary>Builds the classifier over one mailbox's settings, every other mailbox classifying nothing.</summary>
    /// <param name="settings">What the classified mailbox asked for.</param>
    /// <param name="facts">What the header reader answers with.</param>
    /// <param name="email">The message the reader resolves, which carries the mailbox it is in.</param>
    /// <param name="junkFolders">Which folder each account files junk into.</param>
    /// <param name="withEmail">Whether the reader resolves the message at all.</param>
    /// <param name="withContent">Whether the content store holds its mail.</param>
    /// <param name="classifying">Whose mailbox <paramref name="settings" /> belong to, defaulting to the one the message is in.</param>
    /// <returns>The classifier under test.</returns>
    /// <remarks>
    /// The settings are answered for one account rather than for whichever one is asked about, because that is what the
    /// deployed reader does: an account its roster does not resolve classifies nothing. A double answering every
    /// account alike would let a classifier that read the wrong mailbox's settings pass every test here.
    /// </remarks>
    private EmailSpamClassifier Classifier(
        SpamClassificationSettings settings,
        SpamHeaderFacts facts,
        ClassifiableEmail? email = null,
        StubJunkMailFolderCatalog? junkFolders = null,
        bool withEmail = true,
        bool withContent = true,
        MailAccountId? classifying = null)
    {
        var emailReader = Substitute.For<IClassifiableEmailReader>();
        emailReader
            .FindAsync(Arg.Any<MailAccountId>(), Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(withEmail ? email ?? new ClassifiableEmail(Occurrence, Account, Inbox) : null);

        this.contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(withContent ? SomeContent() : null);

        var headerReader = Substitute.For<IEmailSpamHeaderReader>();
        headerReader.ReadAsync(Arg.Any<StoredEmailContent>(), Arg.Any<CancellationToken>()).Returns(facts);

        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.SettingsFor(Arg.Any<MailAccountId>()).Returns(SpamClassificationSettings.Disabled);
        settingsReader.SettingsFor(classifying ?? Account).Returns(settings);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var timeProvider = new FakeTimeProvider(EvaluatedAt);

        return new EmailSpamClassifier(
            emailReader,
            this.contentStore,
            headerReader,
            junkFolders ?? StubJunkMailFolderCatalog.None,
            new DeterministicSpamClassifier(),
            settingsReader,
            this.store,
            this.chunkStore,
            this.attachmentTextStore,
            this.gateTelemetry,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                timeProvider),
            timeProvider);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
