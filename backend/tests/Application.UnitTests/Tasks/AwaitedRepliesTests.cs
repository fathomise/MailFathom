// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Signals;
using MailFathom.Application.Tasks;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Tasks;

/// <summary>
/// Covers what closes a wait: a reply that is somebody else's, in the thread the wait is anchored to, from the person
/// the wait names where it names one. The user's own messages, their filed copies, and mail threading placed nowhere
/// answer nothing, and a person is told once however many of their waits one arrival closed.
/// </summary>
public sealed class AwaitedRepliesTests
{
    private const string OwnAddress = "jan@example.test";

    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly UserId User = SyntheticUser.Deployment;

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid ThreadValue = new("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92");

    private static readonly EmailThreadId Thread = EmailThreadId.Create(ThreadValue);

    private readonly FakeTimeProvider clock = new(Now);

    private readonly InMemoryPersonalTaskStore store = new();

    private readonly InMemoryStoredEmailSummaries summaries = new();

    private readonly RecordingClientSignalChannel channel = new();

    [Fact]
    public async Task AnswerAsync_TheCounterpartRepliesInTheThread_ClosesTheTaskCitingTheReply()
    {
        // Arrange
        var task = await this.WaitAsync("Buyer@Example.test");
        var reply = this.Arrive("buyer@example.test", Now.AddHours(2));

        // Act
        await this.AnswerAsync(reply);

        // Assert
        var closed = Assert.Single(this.store.Held, held => held.Id == task.Id);

        Assert.True(closed.IsCompleted);
        Assert.Equal(new TaskAnswer(reply, Now.AddHours(2)), closed.Answer);
    }

    [Fact]
    public async Task AnswerAsync_TheUsersOwnMessage_LeavesTheTaskOpen()
    {
        // Arrange
        await this.WaitAsync(counterpart: null);
        var own = this.Arrive("JAN@example.test", Now);

        // Act
        await this.AnswerAsync(own);

        // Assert
        Assert.False(Assert.Single(this.store.Held).IsCompleted);
    }

    [Fact]
    public async Task AnswerAsync_AFiledSentCopy_LeavesTheTaskOpen()
    {
        // Arrange
        await this.WaitAsync(counterpart: null);
        var copy = this.Arrive("buyer@example.test", Now, isFiledSentCopy: true);

        // Act
        await this.AnswerAsync(copy);

        // Assert
        Assert.False(Assert.Single(this.store.Held).IsCompleted);
    }

    [Fact]
    public async Task AnswerAsync_SomebodyElseRepliesWhileACounterpartIsNamed_LeavesTheTaskOpen()
    {
        // Arrange
        await this.WaitAsync("buyer@example.test");
        var other = this.Arrive("colleague@example.test", Now);

        // Act
        await this.AnswerAsync(other);

        // Assert
        Assert.False(Assert.Single(this.store.Held).IsCompleted);
    }

    [Fact]
    public async Task AnswerAsync_ATaskWithNoCounterpart_IsClosedByAnyOtherSender()
    {
        // Arrange
        await this.WaitAsync(counterpart: null);
        var reply = this.Arrive("colleague@example.test", Now);

        // Act
        await this.AnswerAsync(reply);

        // Assert
        Assert.True(Assert.Single(this.store.Held).IsCompleted);
    }

    [Fact]
    public async Task AnswerAsync_AMessageWithNoThread_ChangesNothing()
    {
        // Arrange
        await this.WaitAsync(counterpart: null);
        var unthreaded = this.Arrive("colleague@example.test", Now, threaded: false);

        // Act
        await this.AnswerAsync(unthreaded);

        // Assert
        Assert.False(Assert.Single(this.store.Held).IsCompleted);
        Assert.Empty(this.channel.Published);
    }

    [Fact]
    public async Task AnswerAsync_OneUserWithTwoAnsweredTasks_IsSignalledOnce()
    {
        // Arrange
        await this.WaitAsync(counterpart: null);
        await this.WaitAsync(counterpart: null);
        var reply = this.Arrive("colleague@example.test", Now);

        // Act
        await this.AnswerAsync(reply);

        // Assert
        Assert.All(this.store.Held, held => Assert.True(held.IsCompleted));
        Assert.Equal(ClientSignal.TasksChanged(User).Scope, Assert.Single(this.channel.Published).Scope);
    }

    private async Task AnswerAsync(StoredEmailId arrival)
    {
        await using var signals = new ClientSignals([this.channel], this.clock);
        var identities = Substitute.For<IOutgoingSenderIdentityReader>();

        Assert.True(EmailAddress.TryCreate(null, OwnAddress, out var own));
        identities.FindSenderIdentity(Account).Returns(OutgoingSenderIdentity.Create(Account, own));

        await new AwaitedReplies(this.summaries, this.store, identities, signals, this.clock)
            .AnswerAsync(Account, [arrival], TestContext.Current.CancellationToken);

        this.clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();
    }

    private StoredEmailId Arrive(string from, DateTimeOffset receivedAt, bool isFiledSentCopy = false, bool threaded = true)
    {
        var summary = SyntheticEmailSummaries.Create(
            receivedAt: receivedAt,
            accountId: Account.Value,
            senderAddress: from,
            threadId: threaded ? ThreadValue : null) with
        {
            IsFiledSentCopy = isFiledSentCopy,
        };

        this.summaries.With(summary);

        return summary.StoredEmailId;
    }

    private async Task<PersonalTask> WaitAsync(string? counterpart)
    {
        TaskCounterpart? named = null;

        if (counterpart is not null)
        {
            Assert.True(EmailAddress.TryCreate(null, counterpart, out var address));
            named = new TaskCounterpart(address, "The buyer");
        }

        var task = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.CreateVersion7(Now + TimeSpan.FromMinutes(this.store.Held.Count))),
            User,
            "Hear back about the tender",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            new TaskCommitment(PersonalTaskDirection.Awaited, null, null, TaskAnchor.Thread(Thread, "Tender"), named));

        await this.store.AddAsync(task, TestContext.Current.CancellationToken);

        return task;
    }
}
