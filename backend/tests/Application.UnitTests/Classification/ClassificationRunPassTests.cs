// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Categories;
using MailFathom.Application.Classification;
using MailFathom.Application.Decisions;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Model;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Categories;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Classification;

/// <summary>Covers what a pass carries, what it skips, what it leaves for the next one, and how a run stops early.</summary>
public sealed class ClassificationRunPassTests
{
    private static readonly MailAccountId Account =
        MailAccountId.Create("acct-1");

    private static readonly MailAccountId OtherAccount =
        MailAccountId.Create("acct-2");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("ARCHIVE");

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly OwnMailCategory Project = new("Project Heron", "Mail about the Heron building project.");

    private static readonly EmailCategorizationSettings CategorizationOn = new(IsEnabled: true, [Project]);

    private readonly SpamClassificationHarness harness = new(EvaluatedAt);

    private readonly InMemoryClassificationRunStore runs = new();

    private readonly HashSet<StoredEmailId> emailsWithoutContent = [];

    private readonly IEmailCategoryStore categoryStore = Substitute.For<IEmailCategoryStore>();

    private readonly ScriptedEmailCategoryJudge categoryJudge = new()
    {
        Answer = EmailCategoryJudgement.Answered(
            [new(EmailCategoryKind.Default, DefaultMailCategory.Finance.ToString(), 0.9, "vendor/decider-1", "default-revision")]),
    };

    private int storedEmailCount;

    /// <summary>Stores content for every occurrence but the ones a test says nothing is stored for.</summary>
    public ClassificationRunPassTests()
    {
        this.harness.Assignments.Assigning(SyntheticUser.Deployment, Account, OtherAccount);
        this.harness.ContentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => this.emailsWithoutContent.Contains(call.Arg<StoredEmailId>())
                ? null
                : SpamClassificationHarness.SomeContent());
    }

    [Fact]
    public async Task RunAsync_NoRunOutstanding_ReportsNoneAndReadsNoMail()
    {
        // Arrange
        this.StoreEmail();
        var pass = this.CreatePass();

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(report.Walk);
        Assert.Null(report.Ending);
        Assert.Empty(this.harness.Emails.RequestedBatchSizes);
        Assert.Empty(this.runs.Saves);
    }

    [Fact]
    public async Task RunAsync_ARequestedRun_ClassifiesTheScopeAndCompletes()
    {
        // Arrange
        this.StoreEmail();
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(2, report.Walk?.SpamEmailCount);
        Assert.False(report.Walk?.EmailsRemain);
        Assert.Equal(ClassificationRunEnding.Completed, report.Ending);
        Assert.Equal(ClassificationRunEnding.Completed, this.runs.Find(Account)?.Ending);
        Assert.Equal(EvaluatedAt, this.runs.Find(Account)?.EndedAt);
        Assert.Equal(2, this.harness.Classifications.Saved.Count);
    }

    /// <summary>The run binds what it is decided under, so a later pass can tell terms that moved from terms that did not.</summary>
    [Fact]
    public async Task RunAsync_APassPickingTheRunUp_BindsTheProfileItStartedUnder()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SettingsCovering(Inbox).Profile, report.Profile);
        Assert.Equal(SettingsCovering(Inbox).Profile, this.runs.Find(Account)?.Profile);
    }

    /// <summary>Skipping the scoring is the saving; skipping the action would make switching filing on file nothing.</summary>
    [Fact]
    public async Task RunAsync_MailAlreadyDecidedUnderTheRunsProfile_SkipsTheScoringAndStillActsOnIt()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.harness.Classifications.Hold(ClassificationOf(stored, SettingsCovering(Inbox).Profile));
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass(SpamActionSettings.Create(
            filesJunk: false,
            marksJunkRead: true)).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.SkippedEmailCount);
        Assert.Equal(0, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(1, report.Walk?.SpamEmailCount);
        Assert.Equal(1, report.Walk?.ActedEmailCount);
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(1, this.harness.Mutations.OpenedRecordCount);
    }

    [Fact]
    public async Task RunAsync_ARunThatRescores_ScoresMailAlreadyDecidedUnderItsProfile()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.harness.Classifications.Hold(ClassificationOf(stored, SettingsCovering(Inbox).Profile));
        this.runs.Arrange(this.RequestedRun(rescores: true));

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(0, report.Walk?.SkippedEmailCount);
        Assert.Single(this.harness.Classifications.Saved);
    }

    /// <summary>A record from before the profile was part of one names no profile the run can compare, so it is reached again.</summary>
    [Fact]
    public async Task RunAsync_ARecordNamingNoProfile_IsScoredAgainRatherThanSkipped()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.harness.Classifications.Hold(ClassificationOf(stored, profile: default));
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Single(this.harness.Classifications.Saved);
    }

    [Fact]
    public async Task RunAsync_ARecordDecidedUnderOtherTerms_IsScoredAgainRatherThanSkipped()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.harness.Classifications.Hold(
            ClassificationOf(stored, SpamClassificationProfile.Restore("a67d92672a05")));
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Single(this.harness.Classifications.Saved);
    }

    /// <summary>A profile change re-decides mail, and the person's own verdict is the mail it never reaches — but still acts on.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ThePersonsVerdictUnderOtherTerms_StandsIsCountedAsSkippedAndIsStillActedOn(bool rescores)
    {
        // Arrange
        var stored = this.StoreEmail();
        var personsVerdict = SpamClassification.Create(
            stored,
            SpamVerdict.Spam,
            SpamClassificationStage.Person,
            assessment: null,
            SpamClassificationProfile.Restore("a67d92672a05"),
            [],
            EvaluatedAt.AddDays(-1),
            SpamSubcategory.Scam);

        this.harness.Classifications.Hold(personsVerdict);
        this.runs.Arrange(this.RequestedRun(rescores: rescores));

        // Act
        var report = await this.CreatePass(SpamActionSettings.Create(
            filesJunk: false,
            marksJunkRead: true)).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(1, report.Walk?.SkippedEmailCount);
        Assert.Equal(1, report.Walk?.SpamEmailCount);
        Assert.Equal(1, report.Walk?.ActedEmailCount);
        Assert.Equal(1, this.harness.Mutations.OpenedRecordCount);
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Same(personsVerdict, await this.harness.Classifications.FindAsync(stored, TestContext.Current.CancellationToken));
    }

    /// <summary>A run cannot finish under terms it did not start with, and half a mailbox decided both ways is worse.</summary>
    [Fact]
    public async Task RunAsync_TheProfileMovedWhileARunWasOutstanding_EndsItAsSupersededWithoutClassifying()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun() with
        {
            Profile = SpamClassificationProfile.Restore("a67d92672a05"),
        });

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationRunEnding.Superseded, report.Ending);
        Assert.Equal(ClassificationRunEnding.Superseded, this.runs.Find(Account)?.Ending);
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Empty(this.harness.Emails.RequestedBatchSizes);
    }

    [Fact]
    public async Task RunAsync_ClassificationSwitchedOffWhileARunWasOutstanding_EndsItAsDisabled()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass(settings: SpamClassificationSettings.Disabled)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationRunEnding.Disabled, report.Ending);
        Assert.Equal(ClassificationRunEnding.Disabled, this.runs.Find(Account)?.Ending);
        Assert.Empty(this.harness.Classifications.Saved);
    }

    [Fact]
    public async Task RunAsync_MoreMailThanThePassBudgetReaches_CommitsWhatItReachedAndLeavesTheRest()
    {
        // Arrange
        var stored = Enumerable.Range(0, 5).Select(_ => this.StoreEmail()).ToArray();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass(batchSize: 1, maxBatchesPerPass: 2)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.Walk?.ClassifiedEmailCount);
        Assert.True(report.Walk?.EmailsRemain);
        Assert.Null(report.Ending);
        Assert.Equal(stored[1], this.runs.Find(Account)?.Position);
        Assert.True(this.runs.Find(Account)?.IsOutstanding);
    }

    [Fact]
    public async Task RunAsync_ARunLeftPartWayThrough_ResumesFromTheCommittedPosition()
    {
        // Arrange
        var stored = Enumerable.Range(0, 3).Select(_ => this.StoreEmail()).ToArray();
        this.runs.Arrange(this.RequestedRun() with
        {
            Profile = SettingsCovering(Inbox).Profile,
            Position = stored[0],
            ClassifiedEmailCount = 1,
        });

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [stored[1], stored[2]],
            this.harness.Classifications.Saved.Select(classification => classification.EmailId));
        Assert.Equal(2, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(3, this.runs.Find(Account)?.ClassifiedEmailCount);
    }

    /// <summary>The rehearsal the run exists for: what it would file is counted and the mailbox is asked for nothing.</summary>
    [Fact]
    public async Task RunAsync_ADryRun_AsksTheMailboxForNothingAndStillCountsWhatItWouldDo()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun(posture: SpamActionPosture.DryRun));

        // Act
        var report = await this.CreatePass(SpamActionSettings.Create(
            filesJunk: false,
            marksJunkRead: true)).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ActedEmailCount);
        Assert.Equal(0, this.harness.Mutations.OpenedRecordCount);
    }

    /// <summary>Mail whose content is not stored is an answer rather than a failure, and it stops no walk.</summary>
    [Fact]
    public async Task RunAsync_MailWhoseContentIsNotStored_CountsItAsUnclassifiableAndCarriesOn()
    {
        // Arrange
        var unreadable = this.StoreEmail();
        this.emailsWithoutContent.Add(unreadable);
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.UnclassifiableEmailCount);
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(ClassificationRunEnding.Completed, report.Ending);
    }

    [Fact]
    public async Task RunAsync_MailOutsideTheRunsScopeOrOfAnotherAccount_IsNotReached()
    {
        // Arrange
        var inScope = this.StoreEmail();
        this.StoreEmail(Archive);
        this.StoreEmail(accountId: OtherAccount);
        this.runs.Arrange(this.RequestedRun());

        // Act
        await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([inScope], this.harness.Classifications.Saved.Select(classification => classification.EmailId));
    }

    [Fact]
    public async Task RunAsync_BothFunctions_DecidesEveryMessageOfTheBatchForSpamBeforeLookingAtCategories()
    {
        // Arrange
        this.StoreEmail();
        this.StoreEmail();
        this.OfferEveryMessageForAsking();
        var verdictsWhenCategoriesWereRead = -1;
        this.categoryStore
            .When(store => store.GetStoredEmailsToCategorizeAsync(Account, Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>()))
            .Do(_ => verdictsWhenCategoriesWereRead = this.harness.Classifications.Saved.Count);
        this.runs.Arrange(this.RequestedRun(categoryFolders: [Inbox]));

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, verdictsWhenCategoriesWereRead);
        Assert.Equal(ClassificationRunEnding.Completed, report.Ending);
        Assert.Equal(2, this.runs.Find(Account)!.CategorizedEmailCount);
        Assert.Equal(2, this.runs.Find(Account)!.ClassifiedEmailCount);
    }

    [Fact]
    public async Task RunAsync_SpamOnly_LeavesCategoriesAlone()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(this.harness.Classifications.Saved);
        await this.categoryStore.DidNotReceiveWithAnyArgs().GetStoredEmailsToCategorizeAsync(default, default!, TestContext.Current.CancellationToken);
        Assert.Empty(this.categoryJudge.Asked);
    }

    /// <summary>Spam classification is off here, so nothing scores the mail, and the profile of a switch that is off binds nothing.</summary>
    [Fact]
    public async Task RunAsync_CategoriesOnly_AsksWithoutScoringAndBindsNoProfile()
    {
        // Arrange
        this.StoreEmail();
        this.OfferEveryMessageForAsking();
        this.runs.Arrange(this.RequestedRun(spamFolders: [], categoryFolders: [Inbox]));

        // Act
        var report = await this.CreatePass(settings: SpamClassificationSettings.Disabled)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationRunEnding.Completed, report.Ending);
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Single(this.categoryJudge.Asked);
        Assert.False(this.runs.Find(Account)!.Profile.IsSpecified);
    }

    /// <summary>A folder only one function reaches gets only that half, and is not reported as mail spam could reach no verdict about.</summary>
    [Fact]
    public async Task RunAsync_AFolderOnlyCategoriesCover_IsNotScoredAndNotCountedUnclassifiable()
    {
        // Arrange
        var inArchive = this.StoreEmail(Archive);
        this.OfferEveryMessageForAsking();
        this.runs.Arrange(this.RequestedRun(spamFolders: [Inbox], categoryFolders: [Inbox, Archive]));

        // Act
        await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(0, this.runs.Find(Account)!.UnclassifiableEmailCount);
        await this.categoryStore.Received(1).GetStoredEmailsToCategorizeAsync(
            Account,
            Arg.Is<IReadOnlyList<StoredEmailId>>(ids => ids.SequenceEqual(new[] { inArchive })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AFolderOnlySpamCovers_NeverReachesTheCategoryHalf()
    {
        // Arrange
        this.StoreEmail(Archive);
        this.runs.Arrange(this.RequestedRun(spamFolders: [Archive], categoryFolders: [Inbox]));

        // Act
        await this.CreatePass(settings: SettingsCovering(Archive)).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(this.harness.Classifications.Saved);
        await this.categoryStore.DidNotReceiveWithAnyArgs().GetStoredEmailsToCategorizeAsync(default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_OneOwnCategory_AsksNothingAboutSpamAndNothingButThatQuestion()
    {
        // Arrange
        this.StoreEmail();
        this.OfferEveryMessageForAsking();
        this.runs.Arrange(this.RequestedRun(
            posture: SpamActionPosture.DryRun,
            spamFolders: [],
            categoryFolders: [Inbox],
            ownCategory: Project.Name));

        // Act
        await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal([Project], this.categoryJudge.AskedAbout);
        Assert.Empty(this.categoryJudge.Asked);
    }

    [Fact]
    public async Task RunAsync_TheCategoryHalfSwitchedOffWhileOutstanding_EndsAsDisabledWithoutReading()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun(categoryFolders: [Inbox]));

        // Act
        var report = await this.CreatePass(categorizationSettings: EmailCategorizationSettings.Disabled)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationRunEnding.Disabled, report.Ending);
        Assert.Empty(this.harness.Emails.RequestedBatchSizes);
    }

    /// <summary>Only what a run covers can end it: a switch it never relied on says nothing about it.</summary>
    [Fact]
    public async Task RunAsync_AFunctionTheRunDoesNotCoverSwitchedOff_CarriesOn()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass(categorizationSettings: EmailCategorizationSettings.Disabled)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationRunEnding.Completed, report.Ending);
    }

    [Fact]
    public async Task RunAsync_TheOwnCategoryItAppliesRemovedWhileOutstanding_EndsAsDisabled()
    {
        // Arrange
        this.runs.Arrange(this.RequestedRun(
            posture: SpamActionPosture.DryRun,
            spamFolders: [],
            categoryFolders: [Inbox],
            ownCategory: Project.Name));

        // Act
        var report = await this.CreatePass(categorizationSettings: new EmailCategorizationSettings(IsEnabled: true, []))
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationRunEnding.Disabled, report.Ending);
    }

    /// <summary>A failed provider stops the asking for the pass, and the walk still reaches the end of its mail.</summary>
    [Fact]
    public async Task RunAsync_AProviderThatStopsAnswering_FinishesTheWalkAndCountsTheUnanswered()
    {
        // Arrange
        this.StoreEmail();
        this.StoreEmail();
        this.OfferEveryMessageForAsking();
        this.categoryJudge.Answer = EmailCategoryJudgement.Unanswered(DecisionModelOutcome.ProviderUnavailable);
        this.runs.Arrange(this.RequestedRun(categoryFolders: [Inbox]));

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClassificationRunEnding.Completed, report.Ending);
        Assert.Equal(2, this.runs.Find(Account)!.CategoriesUnansweredEmailCount);
        Assert.Single(this.categoryJudge.Asked);
    }

    /// <summary>The replica that loses the account's lease has the pass cancelled: it commits no position, and the holder after it reaches the batch again.</summary>
    [Fact]
    public async Task RunAsync_TheReplicaThatLosesTheLeaseInsideABatch_CommitsNoPositionAndTheNextHolderResumesIt()
    {
        // Arrange
        this.StoreEmail();
        this.OfferEveryMessageForAsking();
        this.runs.Arrange(this.RequestedRun(categoryFolders: [Inbox]));
        using var losesTheLease = new CancellationTokenSource();
        this.categoryJudge.OnAsked = losesTheLease.Cancel;

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => this.CreatePass().RunAsync(Account, losesTheLease.Token));

        var committedByTheLoser = this.runs.Saves.Count;

        this.categoryJudge.OnAsked = null;
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, committedByTheLoser);
        Assert.Equal(ClassificationRunEnding.Completed, report.Ending);
        Assert.Equal(1, this.runs.Find(Account)!.CategorizedEmailCount);
    }

    private static SpamClassificationSettings SettingsCovering(params MailFolderAlias[] aliases) =>
        SpamClassificationSettings.Create(isEnabled: true, aliases);

    /// <summary>A record an earlier pass would have left, under the terms the test names.</summary>
    private static SpamClassification ClassificationOf(StoredEmailId emailId, SpamClassificationProfile profile) =>
        SpamClassification.Create(
            emailId,
            SpamVerdict.Spam,
            SpamClassificationStage.Deterministic,
            assessment: null,
            profile,
            [],
            EvaluatedAt.AddDays(-1));

    /// <summary>Stores one occurrence the walk can reach, under an identity that orders after the ones before it.</summary>
    private StoredEmailId StoreEmail(MailFolderAlias? folderAlias = null, MailAccountId? accountId = null) =>
        this.harness.Emails.Add(new ClassifiableEmail(
            StoredEmailId.Create(Guid.Parse($"0199a0c0-0000-7000-8000-{++this.storedEmailCount:D12}")),
            accountId ?? Account,
            folderAlias ?? Inbox,
            folderAlias ?? Inbox,
            EmailKeywords.None,
            IsSettled: true));

    private ClassificationRun RequestedRun(
        SpamActionPosture posture = SpamActionPosture.Acting,
        bool rescores = false,
        MailFolderAlias[]? spamFolders = null,
        MailFolderAlias[]? categoryFolders = null,
        string? ownCategory = null)
    {
        return new ClassificationRun
        {
            Account = Account,
            RequestedAt = EvaluatedAt.AddMinutes(-1),
            Terms = ClassificationRunTerms.Create(spamFolders ?? [Inbox], categoryFolders ?? [], posture, rescores, ownCategory),
        };
    }

    /// <summary>Has the store offer every message of the batch for asking, each holding nothing yet.</summary>
    private void OfferEveryMessageForAsking()
    {
        var content = Encoding.UTF8.GetBytes("Subject: A message\r\n\r\nA body.");

        this.categoryStore
            .GetStoredEmailsToCategorizeAsync(Account, Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<StoredEmailToCategorize>)
            [
                .. call.ArgAt<IReadOnlyList<StoredEmailId>>(1).Select(static email => new StoredEmailToCategorize(
                    new EmailAwaitingCategorization(email, ConversationOpening: null),
                    [])),
            ]);
        this.harness.ContentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => this.emailsWithoutContent.Contains(call.Arg<StoredEmailId>())
                ? null
                : new StoredEmailContent(content, content.Length, SHA256.HashData(content)));
    }

    private ClassificationRunPass CreatePass(
        SpamActionSettings? actions = null,
        SpamClassificationSettings? settings = null,
        EmailCategorizationSettings? categorizationSettings = null,
        int batchSize = 50,
        int maxBatchesPerPass = 4)
    {
        var classificationSettings = settings ?? SettingsCovering(Inbox);

        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.SettingsFor(Arg.Any<MailAccountId>()).Returns(classificationSettings);

        var categorizationSettingsReader = Substitute.For<IEmailCategorizationSettingsReader>();
        categorizationSettingsReader.SettingsFor(Arg.Any<MailAccountId>())
            .Returns(categorizationSettings ?? CategorizationOn);

        var sessionFactory = this.harness.CommittingSessions();
        var commitPolicy = this.harness.CommitPolicyOver(sessionFactory);

        return new ClassificationRunPass(
            this.runs,
            this.harness.Emails,
            this.harness.Classifications,
            this.harness.CreateClassifier(settingsReader, commitPolicy),
            this.harness.CreateActionRecorder(
                actions ?? SpamActionSettings.None,
                SpamClassificationHarness.OccurrenceReader(Account, Inbox),
                sessionFactory,
                commitPolicy),
            settingsReader,
            categorizationSettingsReader,
            new StoredEmailCategorization(
                this.categoryStore,
                this.categoryJudge,
                new EmailCategorizationConsultation(
                    this.categoryJudge,
                    this.harness.ContentStore,
                    new SpamModelEvidenceReader(
                        new BoundedBodyEmailContentRenderer("A short synthetic body."),
                        StubEmailProvenanceReader.AllKnownCorrespondents,
                        SpamModelStageBounds.Default),
                    Substitute.For<IEmailCategorizationTelemetry>()),
                SensitiveContentEgressGuards.Inactive(),
                commitPolicy),
            commitPolicy,
            new ClassificationRunOptions { BatchSize = batchSize, MaxBatchesPerPass = maxBatchesPerPass },
            this.harness.Clock);
    }
}
