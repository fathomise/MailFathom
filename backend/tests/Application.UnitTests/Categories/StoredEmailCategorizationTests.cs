// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Categories;
using MailFathom.Application.Decisions;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Model;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Categories;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Categories;

/// <summary>Covers what the category half of a run asks about, what it replaces, and that no failure stops the walk.</summary>
/// <remarks>Which messages may be asked about at all is the store's predicate, and is asserted where that predicate lives.</remarks>
public sealed class StoredEmailCategorizationTests
{
    private const string DefaultRevision = "default-revision";

    private const string OwnRevision = "own-revision";

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly OwnMailCategory Project = new("Project Heron", "Mail about the Heron building project.");

    private static readonly EmailCategoryAssignment Finance = new(
        EmailCategoryKind.Default,
        DefaultMailCategory.Finance.ToString(),
        0.9,
        "vendor/decider-1",
        DefaultRevision);

    private static readonly EmailCategoryAssignment Heron = new(
        EmailCategoryKind.Own,
        Project.Name,
        0.8,
        "vendor/decider-1",
        OwnRevision);

    private static EmailCategoryRecord CurrentDefault { get; } =
        new(EmailCategoryKind.Default, DefaultMailCategory.Finance.ToString(), DefaultRevision);

    [Fact]
    public async Task CarryAsync_AMessageAnsweringTheCurrentQuestions_CostsNoCall()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Candidate([CurrentDefault, new(EmailCategoryKind.Own, Project.Name, OwnRevision)]);

        // Act
        var report = await fixture.CarryAsync();

        // Assert
        Assert.Equal(1, report.CurrentEmailCount);
        Assert.Empty(fixture.Judge.Asked);
    }

    [Fact]
    public async Task CarryAsync_Rescoring_AsksAgainAndReplacesWhatItHeld()
    {
        // Arrange
        var fixture = new Fixture();
        var email = fixture.Candidate([CurrentDefault]);

        // Act
        var report = await fixture.CarryAsync(rescores: true);

        // Assert
        Assert.Equal(1, report.CategorizedEmailCount);
        await fixture.Store.Received(1).ReplaceAsync(
            Arg.Any<IPersistenceSession>(),
            email,
            Arg.Is<IReadOnlyList<EmailCategoryAssignment>>(assignments => assignments.Contains(Finance)),
            null,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("an-older-default", OwnRevision)]
    [InlineData(DefaultRevision, "an-older-own")]
    public async Task CarryAsync_AnAnswerToAnOlderQuestion_IsAskedAgain(string defaultRevision, string ownRevision)
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Candidate(
        [
            new(EmailCategoryKind.Default, DefaultMailCategory.Finance.ToString(), defaultRevision),
            new(EmailCategoryKind.Own, Project.Name, ownRevision),
        ]);

        // Act
        await fixture.CarryAsync();

        // Assert
        Assert.Single(fixture.Judge.Asked);
    }

    [Fact]
    public async Task CarryAsync_AMessageNeverAsked_IsAsked()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Candidate([]);

        // Act
        await fixture.CarryAsync();

        // Assert
        Assert.Single(fixture.Judge.Asked);
    }

    /// <summary>A no leaves no record, so the absence of one says nothing; naming the category on a run is how it is applied.</summary>
    [Fact]
    public async Task CarryAsync_NoRecordOfAnOwnCategory_IsNotAReasonToAskAgain()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Candidate([CurrentDefault]);

        // Act
        await fixture.CarryAsync();

        // Assert
        Assert.Empty(fixture.Judge.Asked);
    }

    [Fact]
    public async Task CarryAsync_ARecordOfACategoryNoLongerDeclared_NeedsNothing()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Candidate([CurrentDefault, new(EmailCategoryKind.Own, "Withdrawn", "any-revision")]);

        // Act
        await fixture.CarryAsync();

        // Assert
        Assert.Empty(fixture.Judge.Asked);
    }

    [Fact]
    public async Task CarryAsync_OneOwnCategory_AsksThatQuestionAloneAndReplacesOnlyItsRecord()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Judge.OwnCategoryAnswer = EmailCategoryJudgement.Answered([Heron]);
        var email = fixture.Candidate([CurrentDefault]);

        // Act
        var report = await fixture.CarryAsync(ownCategory: Project.Name);

        // Assert
        Assert.Equal(1, report.CategorizedEmailCount);
        Assert.Equal([Project], fixture.Judge.AskedAbout);
        Assert.Empty(fixture.Judge.Asked);
        await fixture.Store.Received(1).ReplaceAsync(
            Arg.Any<IPersistenceSession>(),
            email,
            Arg.Is<IReadOnlyList<EmailCategoryAssignment>>(assignments => assignments.SequenceEqual(new[] { Heron })),
            Project.Name,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CarryAsync_OneOwnCategoryAnsweredNo_RemovesItsRecord()
    {
        // Arrange
        var fixture = new Fixture();
        var email = fixture.Candidate([]);

        // Act
        await fixture.CarryAsync(ownCategory: Project.Name);

        // Assert
        await fixture.Store.Received(1).ReplaceAsync(
            Arg.Any<IPersistenceSession>(),
            email,
            Arg.Is<IReadOnlyList<EmailCategoryAssignment>>(assignments => assignments.Count == 0),
            Project.Name,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CarryAsync_SpamInTheBatch_ClearsWhatItHeldNamingTheOneCategoryWhereThereIsOne()
    {
        // Arrange
        var fixture = new Fixture();
        var batch = new[] { StoredEmailId.Create(Guid.CreateVersion7()) };
        fixture.Store
            .ClearCategoriesOfSpamAsync(Arg.Any<IPersistenceSession>(), Account, batch, Project.Name, Arg.Any<CancellationToken>())
            .Returns(1);

        // Act
        var report = await fixture.CarryAsync(batch, ownCategory: Project.Name);

        // Assert
        Assert.Equal(1, report.ClearedEmailCount);
    }

    [Fact]
    public async Task CarryAsync_AFailedConsultation_LeavesThatMessageAloneAndGoesOn()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Judge.Answer = EmailCategoryJudgement.Unanswered(DecisionModelOutcome.AnswerUnreadable);
        fixture.Candidate([CurrentDefault]);
        fixture.Candidate([CurrentDefault]);

        // Act
        var report = await fixture.CarryAsync(rescores: true);

        // Assert
        Assert.Equal(2, report.UnansweredEmailCount);
        Assert.Null(report.AskingStoppedBy);
        Assert.Equal(2, fixture.Judge.Asked.Count);
        await fixture.Store.DidNotReceiveWithAnyArgs().ReplaceAsync(default!, default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>The account's run waits on this half, so a failure about every message is met once rather than per message.</summary>
    [Theory]
    [InlineData(DecisionModelOutcome.BudgetExhausted)]
    [InlineData(DecisionModelOutcome.ProviderUnavailable)]
    [InlineData(DecisionModelOutcome.ScannerUnavailable)]
    [InlineData(DecisionModelOutcome.NotActivated)]
    public async Task CarryAsync_AFailureAboutEveryMessage_StopsAskingAndCountsTheRestAsUnanswered(DecisionModelOutcome outcome)
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Judge.Answer = EmailCategoryJudgement.Unanswered(outcome);
        fixture.Candidate([]);
        fixture.Candidate([]);

        // Act
        var report = await fixture.CarryAsync();

        // Assert
        Assert.Equal(outcome, report.AskingStoppedBy);
        Assert.Equal(2, report.UnansweredEmailCount);
        Assert.Single(fixture.Judge.Asked);
    }

    [Fact]
    public async Task CarryAsync_AskingAlreadyStoppedThisPass_AsksNothingAndStillCountsWhatIsCurrent()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Candidate([]);
        fixture.Candidate([CurrentDefault]);

        // Act
        var report = await fixture.CarryAsync(stoppedBy: DecisionModelOutcome.ProviderUnavailable);

        // Assert
        Assert.Empty(fixture.Judge.Asked);
        Assert.Equal((1, 1), (report.UnansweredEmailCount, report.CurrentEmailCount));
    }

    [Fact]
    public async Task CarryAsync_AMessageWhoseContentIsNotStored_IsUnansweredAndTheWalkGoesOn()
    {
        // Arrange
        var fixture = new Fixture();
        var withoutContent = fixture.Candidate([]);
        fixture.Candidate([]);
        fixture.ContentStore.FindStoredContentAsync(withoutContent, Arg.Any<CancellationToken>()).Returns((StoredEmailContent?)null);

        // Act
        var report = await fixture.CarryAsync();

        // Assert
        Assert.Equal((1, 1), (report.UnansweredEmailCount, report.CategorizedEmailCount));
        Assert.Null(report.AskingStoppedBy);
    }

    private sealed class Fixture
    {
        private readonly List<StoredEmailToCategorize> candidates = [];

        public IEmailCategoryStore Store { get; } = Substitute.For<IEmailCategoryStore>();

        public IEmailContentStore ContentStore { get; } = Substitute.For<IEmailContentStore>();

        public ScriptedEmailCategoryJudge Judge { get; } = new()
        {
            Answer = EmailCategoryJudgement.Answered([Finance]),
            Revisions = new(DefaultRevision, new Dictionary<string, string> { [Project.Name] = OwnRevision }),
        };

        public StoredEmailId Candidate(IReadOnlyList<EmailCategoryRecord> records)
        {
            var email = StoredEmailId.Create(Guid.CreateVersion7());
            var bytes = Encoding.UTF8.GetBytes("Subject: A message\r\n\r\nA body.");

            this.candidates.Add(new StoredEmailToCategorize(new EmailAwaitingCategorization(email, ConversationOpening: null), records));
            this.ContentStore.FindStoredContentAsync(email, Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(bytes, bytes.Length, SHA256.HashData(bytes)));

            return email;
        }

        public Task<StoredEmailCategorizationReport> CarryAsync(
            StoredEmailId[]? batch = null,
            string? ownCategory = null,
            bool rescores = false,
            DecisionModelOutcome? stoppedBy = null)
        {
            this.Store
                .GetStoredEmailsToCategorizeAsync(Account, Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
                .Returns((IReadOnlyList<StoredEmailToCategorize>)this.candidates);

            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => Substitute.For<IPersistenceSession>());

            var categorization = new StoredEmailCategorization(
                this.Store,
                this.Judge,
                new EmailCategorizationConsultation(
                    this.Judge,
                    this.ContentStore,
                    new SpamModelEvidenceReader(
                        new BoundedBodyEmailContentRenderer("A short synthetic body."),
                        StubEmailProvenanceReader.AllKnownCorrespondents,
                        SpamModelStageBounds.Default),
                    Substitute.For<IEmailCategorizationTelemetry>()),
                SensitiveContentEgressGuards.Inactive(),
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), new FakeTimeProvider()));

            return categorization.CarryAsync(
                Account,
                batch ?? [.. this.candidates.Select(static candidate => candidate.Email.Id)],
                new StoredEmailCategorizationTerms(
                    new EmailCategorizationSettings(IsEnabled: true, [Project]),
                    ownCategory,
                    rescores,
                    stoppedBy),
                TestContext.Current.CancellationToken);
        }
    }
}
