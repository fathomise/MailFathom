// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Floor;
using MailFathom.Application.Spam.Runs;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Floor;

public sealed class SpamVerdictFloorPassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("ARCHIVE");

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static readonly SpamClassificationSettings ClassifyingTheInbox =
        SpamClassificationSettings.Create(isEnabled: true, [Inbox]);

    private readonly SpamClassificationHarness harness = new(Now);

    private readonly InMemorySpamVerdictFloorCandidateReader candidates = new();

    private readonly IEmailChunkStore chunkStore = Substitute.For<IEmailChunkStore>();

    private readonly IStoredEmailAttachmentTextStore attachmentTextStore =
        Substitute.For<IStoredEmailAttachmentTextStore>();

    private readonly RecordingDerivedWorkGateTelemetry gateTelemetry = new();

    private readonly IDeploymentMailFolders deploymentFolders = Substitute.For<IDeploymentMailFolders>();

    public SpamVerdictFloorPassTests() => this.deploymentFolders
        .ReadAsync(MailFolderSelection.Junk, Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
        .Returns([new MailFolderIdentity(Account, Junk)]);

    /// <summary>The floor needs no classification switched on and no folder in the classified set.</summary>
    [Fact]
    public async Task RunAsync_AMessageInTheJunkFolderOfAnAccountThatDoesNotClassify_IsGivenSpamWithNoSubcategory()
    {
        // Arrange
        var emailId = this.Store(Junk);
        var pass = this.Pass(SpamClassificationSettings.Disabled);

        // Act
        var raised = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.harness.Classifications.Saved);
        var signal = Assert.Single(saved.Signals);

        Assert.Equal(1, raised);
        Assert.Equal(emailId, saved.EmailId);
        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, saved.DecidedBy);
        Assert.Null(saved.Subcategory);
        Assert.Equal(SpamClassificationProfile.Create(), saved.Profile);
        Assert.Equal(SpamSignalKind.JunkFolderPlacement, signal.Kind);
        Assert.Equal(Now, saved.EvaluatedAt);
    }

    /// <summary>A keyword another client set, on a message in a folder the account does not classify.</summary>
    [Fact]
    public async Task RunAsync_JunkInAFolderOutsideTheClassifiedSet_IsGivenSpamWithTheKeywordAsItsSignal()
    {
        // Arrange
        this.Store(Archive, "$Junk");
        var pass = this.Pass(ClassifyingTheInbox);

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var signal = Assert.Single(Assert.Single(this.harness.Classifications.Saved).Signals);

        Assert.Equal(SpamSignalKind.JunkKeyword, signal.Kind);
        Assert.Equal("$Junk", signal.Name);
    }

    /// <summary>
    /// Withholding a folder from AI withholds nothing from the floor, which is not an AI function: the junk folder is read
    /// by its role alone, and which folders AI may read is never asked.
    /// </summary>
    [Fact]
    public async Task RunAsync_AJunkFolderWithheldFromAI_IsRaisedWithoutAskingWhatAIMayRead()
    {
        // Arrange
        this.Store(Junk);
        this.deploymentFolders
            .ReadAsync(MailFolderSelection.VisibleToAI, Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var pass = this.Pass(ClassifyingTheInbox);

        // Act
        var raised = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, raised);
        await this.deploymentFolders
            .DidNotReceive()
            .ReadAsync(MailFolderSelection.VisibleToAI, Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A model's not-spam on mail in junk is the case the floor exists for: no stage takes such a message below spam.</summary>
    [Fact]
    public async Task RunAsync_AModelsNotSpamOnAMessageInJunk_IsRaisedToSpam()
    {
        // Arrange
        var emailId = this.Store(Junk);
        this.harness.Classifications.Hold(SpamClassification.Create(
            emailId,
            SpamVerdict.NotSpam,
            SpamClassificationStage.Model,
            assessment: null,
            SpamClassificationProfile.Create(namesSubcategoriesOfDeterministicSpam: false, threshold: 0.5),
            [ModelJudgement()],
            Now.AddDays(-1)));
        var pass = this.Pass(ClassifyingTheInbox);

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.harness.Classifications.Saved);

        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, saved.DecidedBy);
    }

    [Fact]
    public async Task RunAsync_ThePersonsNotSpamOnAMessageInJunk_IsLeftAsItIs()
    {
        // Arrange
        var emailId = this.Store(Junk, "$Junk");
        var personsVerdict = SpamClassification.Create(
            emailId,
            SpamVerdict.NotSpam,
            SpamClassificationStage.Person,
            assessment: null,
            SpamClassificationProfile.Create(),
            [],
            Now.AddDays(-1));
        this.harness.Classifications.Hold(personsVerdict);
        var pass = this.Pass(ClassifyingTheInbox);

        // Act
        var raised = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, raised);
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Same(
            personsVerdict,
            await this.harness.Classifications.FindAsync(emailId, TestContext.Current.CancellationToken));
    }

    /// <summary>A second replica reaching a message the first already raised finds the work done and writes nothing.</summary>
    [Fact]
    public async Task RunAsync_AMessageAnotherReplicaAlreadyRaised_IsNotWrittenAgain()
    {
        // Arrange
        var emailId = this.Store(Junk);
        var first = this.Pass(SpamClassificationSettings.Disabled);
        var second = this.Pass(SpamClassificationSettings.Disabled);
        await first.RunAsync(Account, TestContext.Current.CancellationToken);

        // Act
        var raised = await second.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, raised);
        Assert.Single(this.harness.Classifications.Saved);
        Assert.Equal(
            SpamVerdict.Spam,
            (await this.harness.Classifications.FindAsync(emailId, TestContext.Current.CancellationToken))!.Verdict);
    }

    /// <summary>A pair somebody else set counts as neither, and a <c>$NotJunk</c> alone is the person's correction.</summary>
    [Theory]
    [InlineData("ARCHIVE", new[] { "$Junk", "$NotJunk" })]
    [InlineData("JUNK", new[] { "$NotJunk" })]
    [InlineData("ARCHIVE", new[] { "\\Flagged" })]
    public async Task RunAsync_AMessageTheFloorDoesNotReach_IsLeftWithoutAVerdict(string folder, string[] keywords)
    {
        // Arrange
        this.Store(MailFolderAlias.Create(folder), keywords);
        var pass = this.Pass(ClassifyingTheInbox);

        // Act
        var raised = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, raised);
        Assert.Empty(this.harness.Classifications.Saved);
    }

    [Fact]
    public async Task RunAsync_JunkBesideNotJunkInTheJunkFolder_IsRaisedByThePlacement()
    {
        // Arrange
        this.Store(Junk, "$Junk", "$NotJunk");
        var pass = this.Pass(ClassifyingTheInbox);

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var signal = Assert.Single(Assert.Single(this.harness.Classifications.Saved).Signals);

        Assert.Equal(SpamSignalKind.JunkFolderPlacement, signal.Kind);
    }

    /// <summary>
    /// The <c>$Junk</c> beside the person's <c>$NotJunk</c> is this deployment's own, so the <c>$NotJunk</c> stands alone
    /// among the marks somebody else set and the person's correction holds, junk folder or not.
    /// </summary>
    [Fact]
    public async Task RunAsync_AnOwnJunkKeywordBesideThePersonsNotJunkInTheJunkFolder_IsLeftWithoutAVerdict()
    {
        // Arrange
        var emailId = this.Store(Junk, "$Junk", "$NotJunk");
        this.harness.VerdictActs.With(emailId, SpamVerdictAct.MarkedJunk);
        var pass = this.Pass(ClassifyingTheInbox);

        // Act
        var raised = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, raised);
        Assert.Empty(this.harness.Classifications.Saved);
    }

    [Fact]
    public async Task RunAsync_OnAnAccountThatClassifies_RemovesWhatWasDerivedFromTheRaisedMessage()
    {
        // Arrange
        var emailId = this.Store(Archive, "$Junk");
        this.chunkStore
            .DiscardChunksAsync(Arg.Any<IPersistenceSession>(), emailId, Arg.Any<CancellationToken>())
            .Returns(3);
        var pass = this.Pass(ClassifyingTheInbox);

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await this.attachmentTextStore
            .Received(1)
            .DiscardAttachmentTextAsync(Arg.Any<IPersistenceSession>(), emailId, Arg.Any<CancellationToken>());
        Assert.Equal([3], this.gateTelemetry.DiscardedPassageCounts);
    }

    /// <summary>Where nothing gates the cut, passages removed would be cut again, so an account that does not classify keeps them.</summary>
    [Fact]
    public async Task RunAsync_OnAnAccountThatDoesNotClassify_RemovesNothingDerived()
    {
        // Arrange
        this.Store(Junk);
        var pass = this.Pass(SpamClassificationSettings.Disabled);

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await this.chunkStore
            .DidNotReceiveWithAnyArgs()
            .DiscardChunksAsync(Arg.Any<IPersistenceSession>(), Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
        Assert.Empty(this.gateTelemetry.DiscardedPassageCounts);
    }

    /// <summary>One pass is bounded; what it leaves, the next run's pass reaches.</summary>
    [Fact]
    public async Task RunAsync_MoreCandidatesThanOnePassTakes_StopsAtItsBoundResumingEachBatchPastTheLast()
    {
        // Arrange
        StoredEmailId[] stored = [.. Enumerable.Range(0, 5).Select(_ => this.Store(Junk))];
        var pass = this.Pass(
            SpamClassificationSettings.Disabled,
            options: new SpamClassificationRunOptions { BatchSize = 2, MaxBatchesPerPass = 2 });

        // Act
        var raised = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        StoredEmailId[] inOrder = [.. stored.OrderBy(static id => id.Value)];

        Assert.Equal(4, raised);
        Assert.Equal([null, inOrder[1]], this.candidates.RequestedResumptions);
        Assert.Equal(inOrder[..4], this.harness.Classifications.Saved.Select(static saved => saved.EmailId));
    }

    /// <summary>A message the classification still awaits is left to it, and only where the account classifies.</summary>
    [Fact]
    public async Task RunAsync_TheTermsItReadsUnder_NameTheJunkFolderTheClassifiedFoldersAndTheWait()
    {
        // Arrange
        var wait = TimeSpan.FromMinutes(20);
        var classifying = this.Pass(ClassifyingTheInbox, scope: SpamClassificationScope.Create([Account], [], wait));
        var notClassifying = this.Pass(SpamClassificationSettings.Disabled);

        // Act
        await classifying.RunAsync(Account, TestContext.Current.CancellationToken);
        await notClassifying.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                new SpamVerdictFloorTerms([Junk], [Inbox], Now - wait),
                new SpamVerdictFloorTerms([Junk], [], Now - SpamClassificationScope.DefaultMaximumClassificationWait),
            ],
            this.candidates.RequestedTerms,
            TermsComparer.Instance);
    }

    private static SpamSignal ModelJudgement() => SpamSignal.Create(
        SpamSignalKind.ModelJudgement,
        "is-spam",
        "0.12",
        SpamSignalProvenance.FromModel("model-a", "rev-1"));

    private StoredEmailId Store(MailFolderAlias folder, params string[] keywords)
    {
        var emailId = StoredEmailId.Create(Guid.CreateVersion7());

        this.candidates.Add(Account, new SpamVerdictFloorCandidate(emailId, folder, RemoteEmailKeywords.Create(keywords)));

        return emailId;
    }

    private SpamVerdictFloorPass Pass(
        SpamClassificationSettings settings,
        SpamClassificationScope? scope = null,
        SpamClassificationRunOptions? options = null)
    {
        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.SettingsFor(Account).Returns(settings);
        settingsReader
            .ReadScopeInForceAsync(Arg.Any<CancellationToken>())
            .Returns(scope ?? SpamClassificationScope.None);

        return new SpamVerdictFloorPass(
            this.candidates,
            settingsReader,
            this.deploymentFolders,
            this.harness.VerdictActs,
            new DeterministicSpamClassifier(),
            this.harness.Classifications,
            this.chunkStore,
            this.attachmentTextStore,
            this.gateTelemetry,
            this.harness.CommitPolicyOver(this.harness.CommittingSessions()),
            options ?? new SpamClassificationRunOptions(),
            this.harness.Clock);
    }

    private sealed class TermsComparer : IEqualityComparer<SpamVerdictFloorTerms>
    {
        internal static readonly TermsComparer Instance = new();

        public bool Equals(SpamVerdictFloorTerms? x, SpamVerdictFloorTerms? y) =>
            x is not null
            && y is not null
            && x.JunkFolders.SequenceEqual(y.JunkFolders)
            && x.ClassifiedFolders.SequenceEqual(y.ClassifiedFolders)
            && x.AwaitedWhenStoredAfter == y.AwaitedWhenStoredAfter;

        public int GetHashCode(SpamVerdictFloorTerms obj) => obj.AwaitedWhenStoredAfter.GetHashCode();
    }
}
