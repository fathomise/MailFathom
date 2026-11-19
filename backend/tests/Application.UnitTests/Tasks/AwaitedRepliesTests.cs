// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.AutomaticReplies;
using MailFathom.Application.EmailContent.Storage;
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
/// the wait names where it names one, received after the wait began. The user's own messages, their filed copies,
/// mail threading placed nowhere, history a resynchronization settles again, and what a machine sent answer nothing,
/// and a person is told once however many of their waits one arrival closed.
/// </summary>
public sealed class AwaitedRepliesTests
{
    private const string OwnAddress = "jan@example.test";

    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly UserId User = SyntheticUser.Deployment;

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset WaitingSince = Now.AddHours(-1);

    private static readonly Guid ThreadValue = new("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92");

    private static readonly EmailThreadId Thread = EmailThreadId.Create(ThreadValue);

    private readonly FakeTimeProvider clock = new(Now);

    private readonly InMemoryPersonalTaskStore store = new();

    private readonly InMemoryStoredEmailSummaries summaries = new();

    private readonly RecordingClientSignalChannel channel = new();

    private readonly Dictionary<StoredEmailId, EmailAutomation> automations = [];

    /// <summary>The wait ends with the reply that ended it, cited rather than copied, whatever case the address was written in.</summary>
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

    /// <summary>A message the user sent into the thread is not an answer to them.</summary>
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

    /// <summary>The copy of a send filed back into the mailbox is the user's own message however its sender reads.</summary>
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

    /// <summary>A wait on one person is not answered by a third party's message in the same thread.</summary>
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

    /// <summary>A wait naming nobody is answered by whoever else writes in the thread.</summary>
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

    /// <summary>A message threading placed in no conversation can answer no wait, and tells nobody anything.</summary>
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

    /// <summary>Each closed wait tells its person their list changed, and the signals fold into the one an open client refreshes on.</summary>
    [Fact]
    public async Task AnswerAsync_TwoWaitsOfOneUserClosedByOneReply_TellTheUserTheirListChanged()
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

    /// <summary>An account that sends from no address leaves the user's own address unknown, and a message from the person a wait names is still theirs to answer it.</summary>
    [Fact]
    public async Task AnswerAsync_AnAccountWithNoSendingAddressAndTheCounterpartReplies_ClosesTheTask()
    {
        // Arrange
        await this.WaitAsync("buyer@example.test");
        var reply = this.Arrive("buyer@example.test", Now);

        // Act
        await this.AnswerAsync(reply, ownAddress: null);

        // Assert
        Assert.True(Assert.Single(this.store.Held).IsCompleted);
    }

    /// <summary>Without the user's own address nothing tells a reply from their own message, so a wait naming nobody stays open.</summary>
    [Fact]
    public async Task AnswerAsync_AnAccountWithNoSendingAddressAndATaskWithNoCounterpart_LeavesTheTaskOpen()
    {
        // Arrange
        await this.WaitAsync(counterpart: null);
        var reply = this.Arrive("colleague@example.test", Now);

        // Act
        await this.AnswerAsync(reply, ownAddress: null);

        // Assert
        Assert.False(Assert.Single(this.store.Held).IsCompleted);
    }

    /// <summary>A batch costs one read of its summaries however many arrivals it holds.</summary>
    [Fact]
    public async Task AnswerAsync_TwoArrivalsInOneThread_ReadTheirSummariesOnce()
    {
        // Arrange
        await this.WaitAsync("buyer@example.test");
        var first = this.Arrive("colleague@example.test", Now);
        var second = this.Arrive("buyer@example.test", Now.AddHours(1));

        // Act
        await this.AnswerAsync(first, second);

        // Assert
        Assert.Single(this.summaries.Calls);
        Assert.Equal(second, Assert.Single(this.store.Held).Answer?.Message);
    }

    /// <summary>
    /// A resynchronization settles a thread's history again, and a message the counterpart wrote before the person began
    /// waiting is not the reply they wait for.
    /// </summary>
    [Fact]
    public async Task AnswerAsync_AReplyReceivedBeforeTheWaitBegan_LeavesItOpen()
    {
        // Arrange
        await this.WaitAsync("buyer@example.test");
        var history = this.Arrive("buyer@example.test", WaitingSince.AddMinutes(-1));

        // Act
        await this.AnswerAsync(history);

        // Assert
        Assert.False(Assert.Single(this.store.Held).IsCompleted);
        Assert.Empty(this.channel.Published);
    }

    /// <summary>A message no receiving hop dated says nothing about being new, so it answers nothing.</summary>
    [Fact]
    public async Task AnswerAsync_AReplyNoHopDated_LeavesItOpen()
    {
        // Arrange
        await this.WaitAsync("buyer@example.test");
        var undated = this.Arrive("buyer@example.test", receivedAt: null);

        // Act
        await this.AnswerAsync(undated);

        // Assert
        Assert.False(Assert.Single(this.store.Held).IsCompleted);
    }

    /// <summary>A wait reopened after a reply answered it is answered only by a reply after that one, so settling it again closes nothing.</summary>
    [Fact]
    public async Task AnswerAsync_TheReplyAReopenedWaitWasClosedBySettlingAgain_LeavesItOpen()
    {
        // Arrange
        var task = await this.WaitAsync("buyer@example.test");
        var reply = this.Arrive("buyer@example.test", Now);
        await this.AnswerAsync(reply);
        await this.store.SetCompletionAsync(User, task.Id, isCompleted: false, TestContext.Current.CancellationToken);

        // Act
        await this.AnswerAsync(reply);

        // Assert
        var reopened = Assert.Single(this.store.Held);
        Assert.False(reopened.IsCompleted);
        Assert.Equal(Now, reopened.WaitingSince);
    }

    /// <summary>An out-of-office notice or a bulk mailing from the person a wait names is a machine speaking, not the reply.</summary>
    [Theory]
    [InlineData(EmailAutomation.AutomaticallySubmitted)]
    [InlineData(EmailAutomation.BulkPrecedence)]
    public async Task AnswerAsync_WhatAMachineSentFromTheCounterpart_LeavesItOpen(EmailAutomation automation)
    {
        // Arrange
        await this.WaitAsync("buyer@example.test");
        var automatic = this.Arrive("buyer@example.test", Now, automation: automation);

        // Act
        await this.AnswerAsync(automatic);

        // Assert
        Assert.False(Assert.Single(this.store.Held).IsCompleted);
        Assert.Empty(this.channel.Published);
    }

    /// <summary>A posting a mailing list distributed is still a person writing, and a thread on a list is answered through it.</summary>
    [Fact]
    public async Task AnswerAsync_TheCounterpartRepliesThroughAMailingList_ClosesTheTask()
    {
        // Arrange
        await this.WaitAsync("buyer@example.test");
        var posting = this.Arrive("buyer@example.test", Now, automation: EmailAutomation.MailingList);

        // Act
        await this.AnswerAsync(posting);

        // Assert
        Assert.True(Assert.Single(this.store.Held).IsCompleted);
    }

    private Task AnswerAsync(StoredEmailId arrival, string? ownAddress = OwnAddress) =>
        this.AnswerAsync([arrival], ownAddress);

    private Task AnswerAsync(StoredEmailId first, StoredEmailId second) => this.AnswerAsync([first, second], OwnAddress);

    private async Task AnswerAsync(StoredEmailId[] arrivals, string? ownAddress)
    {
        await using var signals = new ClientSignals([this.channel], this.clock);
        var identities = Substitute.For<IOutgoingSenderIdentityReader>();

        if (ownAddress is not null)
        {
            Assert.True(EmailAddress.TryCreate(null, ownAddress, out var own));
            identities.FindSenderIdentity(Account).Returns(OutgoingSenderIdentity.Create(Account, own));
        }

        await new AwaitedReplies(this.summaries, this.ContentNamingEachArrival(), this.EnvelopesStatingTheirAutomation(), this.store, identities, signals)
            .AnswerAsync(Account, arrivals, TestContext.Current.CancellationToken);

        this.clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();
    }

    private static StoredEmailContent ContentNaming(StoredEmailId id)
    {
        var bytes = Encoding.ASCII.GetBytes(id.Value.ToString());

        return new StoredEmailContent(bytes, bytes.Length, SHA256.HashData(bytes));
    }

    private IEmailContentStore ContentNamingEachArrival()
    {
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => ContentNaming(call.ArgAt<StoredEmailId>(0)));

        return contentStore;
    }

    private IArrivedMailEnvelopeReader EnvelopesStatingTheirAutomation()
    {
        var envelopes = Substitute.For<IArrivedMailEnvelopeReader>();
        envelopes
            .Read(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(call => new ArrivedMailEnvelope(
                ReplyTarget: null,
                Author: null,
                this.automations.GetValueOrDefault(
                    StoredEmailId.Create(Guid.Parse(Encoding.ASCII.GetString(call.ArgAt<ReadOnlyMemory<byte>>(0).Span)))),
                HasNullReturnPath: false,
                Subject: null,
                EmailThreadReferences.Create("<reply@example.test>", inReplyTo: null, references: null)));

        return envelopes;
    }

    private StoredEmailId Arrive(
        string from,
        DateTimeOffset? receivedAt,
        bool isFiledSentCopy = false,
        bool threaded = true,
        EmailAutomation automation = EmailAutomation.None)
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
        this.automations[summary.StoredEmailId] = automation;

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
            new TaskCommitment(PersonalTaskDirection.Awaited, null, null, TaskAnchor.Thread(Thread, "Tender"), named),
            WaitingSince);

        await this.store.AddAsync(task, TestContext.Current.CancellationToken);

        return task;
    }
}
