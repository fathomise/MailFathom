// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Editing;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.Rules.Preview;
using MailFathom.Application.Rules.Sets;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Preview;

/// <summary>Covers which stored mail a rule would match, read without committing the rule or acting on any message.</summary>
public sealed class MailRulePreviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountId Account = AccessAuthorizations.ScopedAccount;
    private static readonly MailAccountId OtherAccount = MailAccountId.Create("personal");
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");

    private readonly InMemoryMailRuleSetStore ruleSets = new();
    private readonly InMemoryMailRuleEvaluationStore mail = new();
    private readonly ScriptedMailRuleConditionCompiler compiler = new();
    private readonly IMailRuleActionPermissionReader permissions = Substitute.For<IMailRuleActionPermissionReader>();
    private readonly StubMailFolderMappings folderMappings = StubMailFolderMappings.Nothing;
    private readonly FakeTimeProvider timeProvider = new(Now);

    public MailRulePreviewTests()
    {
        this.permissions
            .GetRuleActionPermissions(Arg.Any<MailAccountId>())
            .Returns(MailRuleActionPermissions.Default);

        this.folderMappings.With(
            Account,
            MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("INBOX/archive")));
    }

    [Fact]
    public async Task PreviewAsync_ACandidateMatchingEveryMessage_ReportsEachOneAndTheActionsItWouldRequest()
    {
        // Arrange
        StoredEmailId[] stored = [.. Enumerable.Range(0, 3).Select(_ => this.mail.Add(FactsFor(Account)))];
        var fileIt = MailRuleAction.Relocate(MailFolderReference.ToAlias(Archive));

        // Act
        var result = await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate("isSeen", [fileIt])),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRulePreviewOutcome.Previewed, result.Outcome);
        Assert.Equal(stored, result.Matches);
        Assert.Equal([fileIt], result.Requested);
        Assert.Equal(3, result.ExaminedEmailCount);
        Assert.Equal(0, result.FailedEmailCount);
        Assert.Null(result.ContinueAfter);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task PreviewAsync_ACandidateMatchingNothing_ExaminesEveryMessageAndReportsNoMatch()
    {
        // Arrange
        this.mail.Add(FactsFor(Account));
        this.mail.Add(FactsFor(Account));

        // Act
        var result = await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate("false")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRulePreviewOutcome.Previewed, result.Outcome);
        Assert.Empty(result.Matches);
        Assert.Equal(2, result.ExaminedEmailCount);
    }

    /// <summary>A preview is a reading: the candidate is never committed and no message is stamped as evaluated.</summary>
    [Fact]
    public async Task PreviewAsync_ACandidate_CommitsNoRuleAndRecordsNoEvaluation()
    {
        // Arrange
        this.mail.Add(FactsFor(Account));

        // Act
        await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate("isSeen")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.ruleSets.Archive);
        Assert.Equal(0, this.ruleSets.LockCount);
        Assert.Empty(this.mail.Evaluated);
    }

    [Fact]
    public async Task PreviewAsync_AnotherAccountsMail_IsNeverExamined()
    {
        // Arrange
        var own = this.mail.Add(FactsFor(Account));
        this.mail.Add(FactsFor(OtherAccount));

        // Act
        var result = await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate("isSeen")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([own], result.Matches);
        Assert.Equal(1, result.ExaminedEmailCount);
    }

    /// <summary>The candidate is judged by the validation every write runs, so a preview cannot show what no write would accept.</summary>
    [Fact]
    public async Task PreviewAsync_ACandidateWhoseConditionDoesNotCompile_IsRefusedWithoutExaminingMail()
    {
        // Arrange
        this.mail.Add(FactsFor(Account));

        // Act
        var result = await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate($"{ScriptedMailRuleConditionCompiler.UnknownFact} == 1")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRulePreviewOutcome.Refused, result.Outcome);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.ExaminedEmailCount);
        Assert.Empty(result.Matches);
        Assert.Empty(result.Requested);
    }

    [Fact]
    public async Task PreviewAsync_ACandidateDeclaringAnActionTheAccountDoesNotPermit_IsRefused()
    {
        // Arrange
        this.mail.Add(FactsFor(Account));

        // Act
        var result = await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate("isSeen", [MailRuleAction.Delete()])),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRulePreviewOutcome.Refused, result.Outcome);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.ExaminedEmailCount);
    }

    [Fact]
    public async Task PreviewAsync_ACandidateFilingIntoAFolderTheAccountDoesNotMap_IsRefused()
    {
        // Arrange
        var nowhere = MailRuleAction.Relocate(MailFolderReference.ToAlias(MailFolderAlias.Create("nowhere")));

        // Act
        var result = await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate("isSeen", [nowhere])),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRulePreviewOutcome.Refused, result.Outcome);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>A rule is previewed before anybody enables it, so a disabled stored rule is read as though it were enabled.</summary>
    [Fact]
    public async Task PreviewAsync_ADisabledStoredRule_IsPreviewedAsThoughItWereEnabled()
    {
        // Arrange
        var arrived = this.mail.Add(FactsFor(Account));
        var rule = Declared("file-invoices", "isSeen") with { Enabled = false };
        this.ruleSets.Arrange(new StoredMailRuleSet(Account, 1, [rule]));

        // Act
        var result = await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfStoredRule(Account, MailRuleReference.To(rule.Id)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRulePreviewOutcome.Previewed, result.Outcome);
        Assert.Equal([arrived], result.Matches);
    }

    [Fact]
    public async Task PreviewAsync_AStoredRuleTheAccountDoesNotHold_IsReportedNotFound()
    {
        // Arrange
        this.mail.Add(FactsFor(Account));
        this.ruleSets.Arrange(new StoredMailRuleSet(Account, 1, [Declared("file-invoices", "isSeen")]));

        // Act
        var result = await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfStoredRule(Account, MailRuleReference.To(MailRuleIds.For("mark-newsletters"))),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRulePreviewOutcome.RuleNotFound, result.Outcome);
        Assert.Equal(0, result.ExaminedEmailCount);
    }

    /// <summary>One request examines a bounded page and names where the next one resumes, so a large mailbox is read in turns.</summary>
    [Fact]
    public async Task PreviewAsync_MoreMailThanOnePageExamines_StopsAtTheBoundAndResumesWhereItStopped()
    {
        // Arrange
        StoredEmailId[] stored =
        [
            .. Enumerable.Range(0, MailRulePreview.MaximumExaminedEmails + 1).Select(_ => this.mail.Add(FactsFor(Account))),
        ];
        var preview = this.CreatePreview();
        var first = await preview.PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate("false")),
            TestContext.Current.CancellationToken);

        // Act
        var next = await preview.PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate("false"), first.ContinueAfter),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailRulePreview.MaximumExaminedEmails, first.ExaminedEmailCount);
        Assert.Equal(stored[MailRulePreview.MaximumExaminedEmails - 1], first.ContinueAfter);
        Assert.Equal(1, next.ExaminedEmailCount);
        Assert.Null(next.ContinueAfter);
    }

    [Fact]
    public async Task PreviewAsync_MoreMatchesThanOnePageReports_StopsAtTheLastReportedMatch()
    {
        // Arrange
        StoredEmailId[] stored =
        [
            .. Enumerable.Range(0, MailRulePreview.MaximumReportedMatches + 50).Select(_ => this.mail.Add(FactsFor(Account))),
        ];

        // Act
        var result = await this.CreatePreview().PreviewAsync(
            MailRulePreviewRequest.OfCandidate(Account, Candidate("isSeen")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(stored.Take(MailRulePreview.MaximumReportedMatches), result.Matches);
        Assert.Equal(MailRulePreview.MaximumReportedMatches, result.ExaminedEmailCount);
        Assert.Equal(stored[MailRulePreview.MaximumReportedMatches - 1], result.ContinueAfter);
    }

    /// <summary>Which messages a rule matches is derived from somebody's mail, so the state read grants nothing towards it.</summary>
    [Fact]
    public async Task PreviewAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedBeforeTheRuleIsRead()
    {
        // Arrange
        this.ruleSets.Arrange(new StoredMailRuleSet(Account, 1, [Declared("file-invoices", "isSeen")]));
        var preview = this.CreatePreview(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => preview.PreviewAsync(
            MailRulePreviewRequest.OfStoredRule(Account, MailRuleReference.To(MailRuleIds.For("file-invoices"))),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminAuditRead, refusal.RequiredPermission);
        Assert.Equal(0, this.compiler.CompileCount);
    }

    private static MailRuleEmailFacts FactsFor(MailAccountId account) =>
        new() { Account = account.Value, Folder = "inbox" };

    private static MailRuleDraft Candidate(string condition, IReadOnlyList<MailRuleAction>? actions = null) => new(
        "file-invoices",
        condition,
        actions ?? [],
        StopWhenMatched: false,
        [MailRuleTrigger.Arrival],
        Schedule: null);

    private static MailRuleDeclaration Declared(string name, string condition) => new(
        MailRuleIds.For(name),
        name,
        Enabled: true,
        condition,
        Actions: [],
        StopWhenMatched: false,
        [MailRuleTrigger.Arrival],
        Schedule: null);

    private MailRulePreview CreatePreview(AccessAuthorization? authorization = null) => new(
        this.ruleSets,
        this.mail,
        new MailRuleSetValidator(this.compiler, this.permissions, this.folderMappings.Resolver),
        this.compiler,
        new MailRuleSetEvaluator(this.timeProvider),
        this.folderMappings,
        MailRuleConditionBounds.Default,
        authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead),
        this.timeProvider);
}
