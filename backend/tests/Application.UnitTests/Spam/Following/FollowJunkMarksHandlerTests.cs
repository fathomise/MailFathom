// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Following;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Following;

public sealed class FollowJunkMarksHandlerTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly StoredEmailId EmailId =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001"));

    private static readonly DateTimeOffset Now = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    private static readonly SpamActionSettings EveryActionOn =
        SpamActionSettings.Create(filesJunk: true, marksJunkRead: true, marksJunkWithKeywords: true);

    private readonly SpamClassificationHarness harness = new(Now);

    public FollowJunkMarksHandlerTests() => this.harness.ContentStore
        .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
        .Returns(_ => SpamClassificationHarness.SomeContent());

    [Fact]
    public void JobType_Always_IsFollowingTheJunkMarks() =>
        Assert.Equal(JobType.FollowJunkMarks, this.Handler([]).JobType);

    /// <summary>Following the source marks nothing read and adds no keyword, or the two sides would argue over the message.</summary>
    [Fact]
    public async Task RunAsync_AnotherClientMarkedTheMessageJunk_RecordsSpamAndAsksTheMailboxForNothing()
    {
        // Arrange
        this.Holding(JunkKeywords.Junk);

        // Act
        await this.Handler([JunkKeywords.Junk]).RunAsync(
            FollowJunkMarksJobPayload.For(Account, EmailId, Now),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamVerdict.Spam, Assert.Single(this.harness.Classifications.Saved).Verdict);
        Assert.Equal(0, this.harness.Mutations.OpenedRecordCount);
    }

    /// <summary>The one change following the source makes: a pair RFC 9051 reads as no statement is taken off, once.</summary>
    [Fact]
    public async Task RunAsync_APairSomebodyElseSet_TakesItOff()
    {
        // Arrange
        string[] pair = [JunkKeywords.Junk, JunkKeywords.NotJunk];
        this.Holding(pair);

        // Act
        await this.Handler(pair).RunAsync(
            FollowJunkMarksJobPayload.For(Account, EmailId, Now),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.harness.Mutations.OpenedRecordCount);
        Assert.Equal([SpamVerdictAct.ClearedJunkMarks], this.harness.VerdictActs.Of(EmailId));
    }

    /// <summary>A person's newer verdict stood, so nothing was concluded that could ask the mailbox for anything.</summary>
    [Fact]
    public async Task RunAsync_ThePersonsNewerVerdictStands_AsksTheMailboxForNothing()
    {
        // Arrange
        string[] pair = [JunkKeywords.Junk, JunkKeywords.NotJunk];
        this.Holding(pair);
        this.harness.Classifications.Hold(SpamClassification.Create(
            EmailId,
            SpamVerdict.NotSpam,
            SpamClassificationStage.Person,
            assessment: null,
            SpamClassificationProfile.Create(),
            [],
            Now.AddHours(1)));

        // Act
        await this.Handler(pair).RunAsync(
            FollowJunkMarksJobPayload.For(Account, EmailId, Now),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(0, this.harness.Mutations.OpenedRecordCount);
    }

    private void Holding(params string[] keywords) => this.harness.Emails.Add(new ClassifiableEmail(
        EmailId,
        Account,
        Inbox,
        Inbox,
        EmailKeywords.Create(keywords),
        IsSettled: true));

    private FollowJunkMarksHandler Handler(string[] keywords)
    {
        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.SettingsFor(Arg.Any<MailAccountId>()).Returns(SpamClassificationSettings.Create(isEnabled: true, [Inbox]));

        var sessionFactory = this.harness.CommittingSessions();
        var commitPolicy = this.harness.CommitPolicyOver(sessionFactory);

        return new FollowJunkMarksHandler(
            this.harness.CreateClassifier(settingsReader, commitPolicy, SpamHeaderFacts.None),
            this.harness.CreateActionRecorder(
                EveryActionOn,
                SpamClassificationHarness.OccurrenceReader(Account, Inbox, keywords),
                sessionFactory,
                commitPolicy));
    }
}
