// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.EmailCategorization;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.SpamClassification;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Categories;
using MailFathom.Application.Decisions;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Categories;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.EmailCategorization;

/// <summary>Covers what one request asks, what comes back from it, and that every failure is an outcome.</summary>
public sealed class EmailCategoryJudgeTests
{
    private const int DeclaredRequestCharacters = 32_000;

    private static readonly OwnMailCategory Project = new("Project Heron", "Mail about the Heron building project.");

    private static readonly OwnMailCategory Allotment = new("Allotment", "Mail about the allotment association.");

    private static EmailCategoryEvidence Evidence { get; } = new(SpamClassificationTurnTests.Evidence(), ConversationOpening: null);

    [Fact]
    public async Task JudgeAsync_OneMessage_AsksTheDefaultChoiceAndOneYesOrNoPerOwnCategoryInOneRequest()
    {
        // Arrange
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var composition = Judging(client);

        // Act
        await composition.Judge.JudgeAsync(Evidence, [Project, Allotment], TestContext.Current.CancellationToken);

        // Assert
        var call = Assert.Single(client.Calls);
        Assert.Equal(3, call.Questions.Count);
        Assert.Equal(DecisionQuestionKind.Choice, call.Questions[0].Kind);
        Assert.Equal(
            DefaultMailCategory.All.Select(static category => category.ToString()),
            call.Questions[0].Options.Select(static option => option.Value));
        Assert.All(call.Questions.Skip(1), static question => Assert.Equal(DecisionQuestionKind.YesNo, question.Kind));
    }

    /// <summary>A name a mailbox gave a subject is the mailbox's; the description is what the question reads.</summary>
    [Fact]
    public async Task JudgeAsync_AnOwnCategory_SendsItsDescriptionAndNeverItsName()
    {
        // Arrange
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var composition = Judging(client);

        // Act
        await composition.Judge.JudgeAsync(Evidence, [Project], TestContext.Current.CancellationToken);

        // Assert
        var question = Assert.Single(client.Calls).Questions[1];
        Assert.DoesNotContain(Project.Name, question.Id + question.Instructions, StringComparison.Ordinal);
        Assert.Contains(question.Options, static option => option.Description == Project.Description);
    }

    [Fact]
    public async Task JudgeAsync_AnAnswer_HoldsExactlyOneDefaultAndEveryOwnCategoryOverTheThreshold()
    {
        // Arrange
        var (projectQuestion, allotmentQuestion) = (EmailCategoryQuestions.For(Project), EmailCategoryQuestions.For(Allotment));
        using var client = ScriptedDecisionClient.Answering((_, _) => Task.FromResult(
            Response(Default(DefaultMailCategory.Finance, 0.7), YesNo(projectQuestion, 0.5), YesNo(allotmentQuestion, 0.49))));
        using var composition = Judging(client);

        // Act
        var judgement = await composition.Judge.JudgeAsync(Evidence, [Project, Allotment], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionModelOutcome.Answered, judgement.Outcome);
        Assert.Collection(
            judgement.Assignments,
            first =>
            {
                Assert.Equal(EmailCategoryKind.Default, first.Kind);
                Assert.Equal(DefaultMailCategory.Finance.ToString(), first.Category);
                Assert.Equal(0.7, first.Probability);
                Assert.Equal("vendor/decider-2026-10-01", first.ModelBuild);
                Assert.Equal(EmailCategoryQuestions.DefaultRevision, first.QuestionRevision);
            },
            second =>
            {
                Assert.Equal(EmailCategoryKind.Own, second.Kind);
                Assert.Equal(Project.Name, second.Category);
                Assert.Equal(DecisionQuestionSet.RevisionOf([projectQuestion]), second.QuestionRevision);
            });
    }

    /// <summary>The revision is per question, and the identifier follows the name, so one rewording or one removal touches one question.</summary>
    [Fact]
    public void Questions_ARewordedOwnCategory_ChangesThatQuestionsRevisionAlone()
    {
        // Arrange
        var reworded = Project with { Description = "Mail about the Heron project's building works." };

        // Act
        var before = DecisionQuestionSet.RevisionOf([EmailCategoryQuestions.For(Project)]);
        var after = DecisionQuestionSet.RevisionOf([EmailCategoryQuestions.For(reworded)]);
        var other = DecisionQuestionSet.RevisionOf([EmailCategoryQuestions.For(Allotment)]);

        // Assert
        Assert.NotEqual(before, after);
        Assert.Equal(other, DecisionQuestionSet.RevisionOf([EmailCategoryQuestions.For(Allotment)]));
        Assert.Equal(EmailCategoryQuestions.For(Project).Id, EmailCategoryQuestions.For(reworded).Id);
    }

    [Fact]
    public async Task JudgeAsync_AnUnansweredOwnQuestion_IsSimplyNotHeld()
    {
        // Arrange
        var question = EmailCategoryQuestions.For(Project);
        using var client = ScriptedDecisionClient.Answering((_, _) => Task.FromResult(
            Response(Default(DefaultMailCategory.Other, 0.9), DecisionAnswer.Unanswered(question.Id))));
        using var composition = Judging(client);

        // Act
        var judgement = await composition.Judge.JudgeAsync(Evidence, [Project], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(judgement.Assignments);
    }

    [Fact]
    public async Task JudgeAsync_AnUnansweredDefaultChoice_IsUnreadableWithNoCategoryAtAll()
    {
        // Arrange
        var question = EmailCategoryQuestions.For(Project);
        using var client = ScriptedDecisionClient.Answering((_, _) => Task.FromResult(
            Response(DecisionAnswer.Unanswered(EmailCategoryQuestions.DefaultQuestionId), YesNo(question, 0.9))));
        using var composition = Judging(client);

        // Act
        var judgement = await composition.Judge.JudgeAsync(Evidence, [Project], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionModelOutcome.AnswerUnreadable, judgement.Outcome);
        Assert.Empty(judgement.Assignments);
    }

    [Fact]
    public async Task JudgeAsync_APeriodAlreadySpent_IsBudgetExhaustedWithoutReachingTheModel()
    {
        // Arrange
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var composition = Judging(client, admitted: false);

        // Act
        var judgement = await composition.Judge.JudgeAsync(Evidence, [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionModelOutcome.BudgetExhausted, judgement.Outcome);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task JudgeAsync_AnUnreachableProvider_IsAnOutcomeRatherThanAFailure()
    {
        // Arrange
        using var client = ScriptedDecisionClient.Throwing(new DecisionFailedException("decider", DecisionFailure.TransportFaulted));
        using var composition = Judging(client);

        // Act
        var judgement = await composition.Judge.JudgeAsync(Evidence, [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionModelOutcome.ProviderUnavailable, judgement.Outcome);
    }

    [Fact]
    public async Task JudgeAsync_AReply_CarriesItsBoundedConversationOpeningInTheTurn()
    {
        // Arrange
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var composition = Judging(client);
        var evidence = Evidence with { ConversationOpening = new ConversationOpening("Can you send the invoice?", IsCut: true) };

        // Act
        await composition.Judge.JudgeAsync(evidence, [], TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.Single(Assert.Single(client.Calls).Input).Text;
        Assert.Contains($"<{EmailCategoryTurn.ConversationOpeningElementName}", written, StringComparison.Ordinal);
        Assert.Contains("Can you send the invoice?", written, StringComparison.Ordinal);
        Assert.Contains($"{AgentTurnDocument.CutAttributeName}=\"true\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JudgeAsync_AMessageWithNoOpening_WritesNoOpeningElement()
    {
        // Arrange
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var composition = Judging(client);

        // Act
        await composition.Judge.JudgeAsync(Evidence, [], TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(
            EmailCategoryTurn.ConversationOpeningElementName,
            Assert.Single(Assert.Single(client.Calls).Input).Text,
            StringComparison.Ordinal);
    }

    /// <summary>The input is cut to what the questions leave, so twenty own categories cannot push a request past the model's bound.</summary>
    [Fact]
    public async Task JudgeAsync_ManyOwnCategories_CutsTheInputToWhatTheQuestionsLeave()
    {
        // Arrange
        using var client = ScriptedDecisionClient.AnsweringFirstOption();
        using var composition = Judging(client, maximumRequestCharacters: 32_000);
        OwnMailCategory[] ownCategories =
        [
            .. Enumerable.Range(0, OwnMailCategoryBounds.MaximumPerAccount)
                .Select(static index => new OwnMailCategory($"Subject {index}", new string('x', 100))),
        ];
        var evidence = Evidence with
        {
            Message = Evidence.Message with { Body = new string('b', 40_000) },
        };

        // Act
        await composition.Judge.JudgeAsync(evidence, ownCategories, TestContext.Current.CancellationToken);

        // Assert
        var call = Assert.Single(client.Calls);
        Assert.True(Assert.Single(call.Input).Text.Length <= 32_000 - DecisionQuestionSet.ReservedCharactersOf(call.Questions));
    }

    [Fact]
    public async Task JudgeOwnCategoryAsync_OneCategory_AsksThatOneQuestionAndNothingElse()
    {
        // Arrange
        var question = EmailCategoryQuestions.For(Project);
        using var client = ScriptedDecisionClient.Answering((_, _) => Task.FromResult(Response(YesNo(question, 0.8))));
        using var composition = Judging(client);

        // Act
        var judgement = await composition.Judge.JudgeOwnCategoryAsync(Evidence, Project, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(question.Id, Assert.Single(Assert.Single(client.Calls).Questions).Id);
        var held = Assert.Single(judgement.Assignments);
        Assert.Equal((EmailCategoryKind.Own, Project.Name), (held.Kind, held.Category));
        Assert.Equal(DecisionQuestionSet.RevisionOf([question]), held.QuestionRevision);
    }

    [Fact]
    public async Task JudgeOwnCategoryAsync_AnsweredNo_IsAnAnswerThatHoldsNothing()
    {
        // Arrange
        var question = EmailCategoryQuestions.For(Project);
        using var client = ScriptedDecisionClient.Answering((_, _) => Task.FromResult(Response(YesNo(question, 0.1))));
        using var composition = Judging(client);

        // Act
        var judgement = await composition.Judge.JudgeOwnCategoryAsync(Evidence, Project, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionModelOutcome.Answered, judgement.Outcome);
        Assert.Empty(judgement.Assignments);
    }

    [Fact]
    public async Task JudgeOwnCategoryAsync_NoAnswerToTheQuestion_IsUnreadable()
    {
        // Arrange
        using var client = ScriptedDecisionClient.Answering((_, _) => Task.FromResult(Response()));
        using var composition = Judging(client);

        // Act
        var judgement = await composition.Judge.JudgeOwnCategoryAsync(Evidence, Project, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionModelOutcome.AnswerUnreadable, judgement.Outcome);
    }

    [Fact]
    public void RevisionsOf_AMailboxsCategories_AnswerTheRevisionsTheirAnswersAreRecordedUnder()
    {
        // Act
        var revisions = InactiveEmailCategoryJudge.Instance.RevisionsOf([Project]);

        // Assert
        Assert.Equal(EmailCategoryQuestions.DefaultRevision, revisions.Default);
        Assert.Equal(DecisionQuestionSet.RevisionOf([EmailCategoryQuestions.For(Project)]), revisions.Own[Project.Name]);
    }

    [Fact]
    public async Task InactiveJudge_AskedAnything_AnswersNotActivated()
    {
        // Act
        var judgement = await InactiveEmailCategoryJudge.Instance.JudgeAsync(Evidence, [], TestContext.Current.CancellationToken);

        // Assert
        var ownJudgement = await InactiveEmailCategoryJudge.Instance.JudgeOwnCategoryAsync(Evidence, Project, TestContext.Current.CancellationToken);

        Assert.False(InactiveEmailCategoryJudge.Instance.IsActive);
        Assert.Equal(DecisionModelOutcome.NotActivated, judgement.Outcome);
        Assert.Equal(DecisionModelOutcome.NotActivated, ownJudgement.Outcome);
    }

    private static DecisionAnswer Default(DefaultMailCategory category, double probability)
    {
        var rest = (1 - probability) / (DefaultMailCategory.All.Count - 1);

        return new DecisionAnswer(
            EmailCategoryQuestions.DefaultQuestionId,
            category.ToString(),
            DefaultMailCategory.All.ToDictionary(static each => each.ToString(), each => each == category ? probability : rest));
    }

    private static DecisionAnswer YesNo(DecisionQuestion question, double yes) => new(
        question.Id,
        yes >= 0.5 ? DecisionQuestion.Yes : DecisionQuestion.No,
        new Dictionary<string, double> { [DecisionQuestion.Yes] = yes, [DecisionQuestion.No] = 1 - yes });

    private static DecisionResponse Response(params DecisionAnswer[] answers) => new(answers) { ModelId = "vendor/decider-2026-10-01" };

    private static Composition Judging(
        ScriptedDecisionClient client,
        bool admitted = true,
        int maximumRequestCharacters = DeclaredRequestCharacters)
    {
        var ledger = Substitute.For<IMailAnsweringSpendLedger>();
        ledger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(admitted);

        var provider = new ServiceCollection()
            .AddSingleton(ledger)
            .AddKeyedScoped<IDecisionClient>(DecisionFunction.EmailCategorization, (_, _) => client)
            .BuildServiceProvider();

#pragma warning disable CA2000 // Composition owns the judge, and every test disposes the composition.
        return new Composition(
            provider,
            new EmailCategoryJudge(
                provider.GetRequiredService<IServiceScopeFactory>(),
                SensitiveContentEgressGuards.Inactive(),
                SpamModelStageBounds.Default,
                maximumRequestCharacters,
                NullLogger<EmailCategoryJudge>.Instance));
#pragma warning restore CA2000
    }

    private sealed class Composition(ServiceProvider provider, EmailCategoryJudge judge) : IDisposable
    {
        public EmailCategoryJudge Judge { get; } = judge;

        public void Dispose()
        {
            this.Judge.Dispose();
            provider.Dispose();
        }
    }
}
