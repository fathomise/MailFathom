// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Categories;
using MailFathom.Application.Decisions;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
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

/// <summary>Covers what a categorization pass asks about, what it writes down, and that no failure reaches past a missing category.</summary>
/// <remarks>Which mail reaches the pass is the store's predicate, and is asserted where that predicate lives.</remarks>
public sealed class EmailCategorizationPassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly OwnMailCategory Project = new("Project Heron", "Mail about the Heron building project.");

    private static readonly EmailCategoryAssignment Finance = new(
        EmailCategoryKind.Default,
        DefaultMailCategory.Finance.ToString(),
        0.9,
        "vendor/decider-1",
        "default-revision");

    [Fact]
    public async Task RunAsync_AnAccountThatDoesNotCategorize_ReadsNothingAndAsksNothing()
    {
        // Arrange
        var fixture = new Fixture { Settings = EmailCategorizationSettings.Disabled };

        // Act
        var report = await fixture.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.IsEmpty);
        await fixture.Store.DidNotReceiveWithAnyArgs().GetEmailsAwaitingCategorizationAsync(default, default, default, TestContext.Current.CancellationToken);
        Assert.Empty(fixture.Judge.Asked);
    }

    [Fact]
    public async Task RunAsync_ADeploymentWithNoModelForCategorization_SaysSoAndReadsNothing()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Judge.IsActive = false;

        // Act
        var report = await fixture.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionModelOutcome.NotActivated, report.StoppedBy);
        Assert.Equal([DecisionModelOutcome.NotActivated], fixture.Telemetry.Outcomes);
        await fixture.Store.DidNotReceiveWithAnyArgs().GetEmailsAwaitingCategorizationAsync(default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_AnAnsweredMessage_RecordsWhatTheModelSaid()
    {
        // Arrange
        var fixture = new Fixture();
        var email = fixture.Awaiting();
        EmailCategoryAssignment own = new(EmailCategoryKind.Own, Project.Name, 0.8, "vendor/decider-1", "own-revision");
        fixture.Judge.Answer = EmailCategoryJudgement.Answered([Finance, own]);

        // Act
        var report = await fixture.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.CategorizedEmailCount);
        Assert.False(report.EmailsRemain);
        Assert.Equal([DecisionModelOutcome.Answered], fixture.Telemetry.Outcomes);
        await fixture.Store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            email.Id,
            Arg.Is<IReadOnlyList<EmailCategoryAssignment>>(saved => saved.SequenceEqual(new[] { Finance, own })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AMessageAndTheAccountsOwnCategories_ReachTheJudgeInOneRequest()
    {
        // Arrange
        var fixture = new Fixture();
        var opening = new ConversationOpening("The first message of the conversation.", IsCut: true);
        fixture.Awaiting(opening);

        // Act
        await fixture.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var (evidence, ownCategories) = Assert.Single(fixture.Judge.Asked);
        Assert.Same(opening, evidence.ConversationOpening);
        Assert.Equal([Project], ownCategories);
    }

    /// <summary>A failure that says something about every message behind this one ends the pass, because this stage holds the account's run while it asks.</summary>
    [Theory]
    [InlineData(DecisionModelOutcome.BudgetExhausted)]
    [InlineData(DecisionModelOutcome.ProviderUnavailable)]
    [InlineData(DecisionModelOutcome.ScannerUnavailable)]
    public async Task RunAsync_AFailureOfTheWholeProvider_EndsThePassWritingNothing(DecisionModelOutcome outcome)
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Awaiting();
        fixture.Awaiting();
        fixture.Judge.Answer = EmailCategoryJudgement.Unanswered(outcome);

        // Act
        var report = await fixture.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(outcome, report.StoppedBy);
        Assert.True(report.EmailsRemain);
        Assert.Single(fixture.Judge.Asked);
        Assert.Equal([outcome], fixture.Telemetry.Outcomes);
        await fixture.Store.DidNotReceiveWithAnyArgs().SaveAsync(default!, default, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>A failure that is this one message's costs this one message its category and nothing else.</summary>
    [Theory]
    [InlineData(DecisionModelOutcome.AnswerUnreadable)]
    [InlineData(DecisionModelOutcome.InputTooLarge)]
    public async Task RunAsync_AFailureOfOneMessage_LeavesItWithNoCategoryAndGoesOn(DecisionModelOutcome outcome)
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Awaiting();
        fixture.Awaiting();
        fixture.Judge.Answer = EmailCategoryJudgement.Unanswered(outcome);

        // Act
        var report = await fixture.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.UnansweredEmailCount);
        Assert.Null(report.StoppedBy);
        Assert.Equal(2, fixture.Judge.Asked.Count);
        await fixture.Store.DidNotReceiveWithAnyArgs().SaveAsync(default!, default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_AMessageWhoseContentIsGone_CountsThatAndAsksNothing()
    {
        // Arrange
        var fixture = new Fixture();
        var email = fixture.Awaiting();
        fixture.ContentStore.FindStoredContentAsync(email.Id, Arg.Any<CancellationToken>()).Returns((StoredEmailContent?)null);

        // Act
        var report = await fixture.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.UnansweredEmailCount);
        Assert.Empty(fixture.Judge.Asked);
        Assert.Equal([DecisionModelOutcome.ContentUnavailable], fixture.Telemetry.Outcomes);
    }

    /// <summary>The pass walks as far as the rule pass behind it, so no message passes that stage unasked.</summary>
    [Fact]
    public async Task RunAsync_AFullBatch_ReadsOnFromWhereItStoppedUntilTheBoundsEnd()
    {
        // Arrange
        var fixture = new Fixture { WalkBounds = new MailRuleEvaluationOptions { BatchSize = 1, MaxBatchesPerPass = 2 } };
        var first = fixture.Awaiting();
        var second = fixture.Awaiting();
        fixture.Awaiting();

        // Act
        var report = await fixture.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.CategorizedEmailCount);
        Assert.True(report.EmailsRemain);
        await fixture.Store.Received(1).GetEmailsAwaitingCategorizationAsync(Account, null, 1, Arg.Any<CancellationToken>());
        await fixture.Store.Received(1).GetEmailsAwaitingCategorizationAsync(Account, first.Id, 1, Arg.Any<CancellationToken>());
        await fixture.Store.DidNotReceive().GetEmailsAwaitingCategorizationAsync(Account, second.Id, 1, Arg.Any<CancellationToken>());
    }

    /// <summary>The replica that loses the account's lease has its token cancelled, and stops between two messages with what it committed left standing.</summary>
    [Fact]
    public async Task RunAsync_TheAccountsLeaseLostBetweenTwoMessages_StopsAskingAndKeepsWhatItCommitted()
    {
        // Arrange
        using var leaseLost = new CancellationTokenSource();
        var fixture = new Fixture();
        var first = fixture.Awaiting();
        fixture.Awaiting();
        fixture.Store.SaveAsync(default!, default, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(_ =>
        {
            leaseLost.Cancel();

            return Task.FromResult(true);
        });

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Pass().RunAsync(Account, leaseLost.Token));

        // Assert
        Assert.Single(fixture.Judge.Asked);
        await fixture.Store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            first.Id,
            Arg.Any<IReadOnlyList<EmailCategoryAssignment>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The replica that took the account may have answered first, and then the store writes nothing and this pass does not fail.</summary>
    [Fact]
    public async Task RunAsync_AnAnswerAnotherReplicaAlreadyRecorded_WritesNothingOverItAndCarriesOn()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Awaiting();
        fixture.Store.SaveAsync(default!, default, default!, TestContext.Current.CancellationToken).ReturnsForAnyArgs(false);

        // Act
        var report = await fixture.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(report.StoppedBy);
        await fixture.Store.ReceivedWithAnyArgs(1).SaveAsync(default!, default, default!, TestContext.Current.CancellationToken);
    }

    private sealed class Fixture
    {
        private readonly List<EmailAwaitingCategorization> queue = [];

        public EmailCategorizationSettings Settings { get; init; } = new(IsEnabled: true, [Project]);

        public MailRuleEvaluationOptions WalkBounds { get; init; } = new();

        public IEmailCategoryStore Store { get; } = Substitute.For<IEmailCategoryStore>();

        public IEmailContentStore ContentStore { get; } = Substitute.For<IEmailContentStore>();

        public ScriptedEmailCategoryJudge Judge { get; } = new() { Answer = EmailCategoryJudgement.Answered([Finance]) };

        public RecordingTelemetry Telemetry { get; } = new();

        public EmailAwaitingCategorization Awaiting(ConversationOpening? opening = null)
        {
            var email = new EmailAwaitingCategorization(StoredEmailId.Create(Guid.CreateVersion7()), opening);
            var bytes = Encoding.UTF8.GetBytes("Subject: A message\r\n\r\nA body.");

            this.queue.Add(email);
            this.ContentStore.FindStoredContentAsync(email.Id, Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(bytes, bytes.Length, SHA256.HashData(bytes)));

            return email;
        }

        public EmailCategorizationPass Pass()
        {
            var settingsReader = Substitute.For<IEmailCategorizationSettingsReader>();
            settingsReader.SettingsFor(Account).Returns(this.Settings);

            this.Store
                .GetEmailsAwaitingCategorizationAsync(Account, Arg.Any<StoredEmailId?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => (IReadOnlyList<EmailAwaitingCategorization>)
                [
                    .. this.queue
                        .SkipWhile(email => call.ArgAt<StoredEmailId?>(1) is { } position && email.Id != position)
                        .Skip(call.ArgAt<StoredEmailId?>(1) is null ? 0 : 1)
                        .Take(call.ArgAt<int>(2)),
                ]);

            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => Substitute.For<IPersistenceSession>());

            return new EmailCategorizationPass(
                this.Store,
                this.Judge,
                settingsReader,
                new EmailCategorizationConsultation(
                    this.Judge,
                    this.ContentStore,
                    new SpamModelEvidenceReader(
                        new BoundedBodyEmailContentRenderer("A short synthetic body."),
                        StubEmailProvenanceReader.AllKnownCorrespondents,
                        SpamModelStageBounds.Default),
                    this.Telemetry),
                SensitiveContentEgressGuards.Inactive(),
                this.Telemetry,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), new FakeTimeProvider()),
                this.WalkBounds);
        }
    }

    private sealed class RecordingTelemetry : IEmailCategorizationTelemetry
    {
        public List<DecisionModelOutcome> Outcomes { get; } = [];

        public void RecordConsultation(DecisionModelOutcome outcome) => this.Outcomes.Add(outcome);
    }
}
