// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Calendar;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Application.Tasks;
using MailFathom.Application.UnitTests.Agent.Conversations;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Scheduling;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Answering;

/// <summary>Covers the grants each declared act asks of whoever accepts it, and what accepting a date or a task leaves behind.</summary>
/// <remarks>
/// <para>
/// The grants are asserted apart from carrying the act out because they are what an acceptance is refused on before
/// anything is recorded: an act asking for less than its use cases do would be recorded as accepted and then fail, and
/// one asking for more would refuse a person the use case itself would have served.
/// </para>
/// <para>
/// A date and a task are carried out through the real calendar and task-list use cases over in-memory stores, since what
/// is asserted is what the person's own calendar and list then hold. The mail use cases are left unset because no act
/// asserted here reaches them; what a sent act does is theirs to prove.
/// </para>
/// </remarks>
public sealed class AgentActPerformerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryCalendarEventStore calendar = new();

    private readonly InMemoryPersonalTaskStore tasks = new();

    private const string SentAccount = "work";

    private readonly FakeTimeProvider clock = new(Start.AddDays(-1));

    /// <summary>Mail leaves the deployment and asks for the grants that send it; a date and a task are this deployment's own record and ask for the grant their own use cases admit.</summary>
    [Theory]
    [MemberData(nameof(ActsAndTheirGrants))]
    public void PermissionsFor_ADeclaredAct_NamesTheGrantsItsUseCasesAsk(
        AgentProposedAct act,
        MailFathomPermission[] expected)
    {
        // Act
        var required = AgentActPerformer.PermissionsFor(act);

        // Assert
        Assert.Equal(expected, required);
    }

    public static TheoryData<AgentProposedAct, MailFathomPermission[]> ActsAndTheirGrants() => new()
    {
        {
            AgentConversationExample.Act(),
            [MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend]
        },
        {
            new AgentResponseSending(
                StoredEmailId.Create(new Guid("44444444-4444-4444-4444-444444444444")),
                AuthoredResponseAct.Reply,
                [],
                PresentationText.Create("Thank you, we accept.")),
            [MailFathomPermission.MailRead, MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend]
        },
        {
            new AgentEventScheduling(
                PresentationText.Create("Renewal call"),
                new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.Zero),
                End: null,
                IsAllDay: false,
                SourceMessage: null),
            [MailFathomPermission.CalendarWrite]
        },
        {
            new AgentTaskRecording(
                PresentationText.Create("Send the revised schedule"),
                new DateOnly(2026, 9, 24),
                SourceMessage: null),
            [MailFathomPermission.TasksWrite]
        },
    };

    /// <summary>An accepted event reaches the calendar exactly as it was proposed, as one the person asserted.</summary>
    [Fact]
    public async Task PerformAsync_AnEventTheCalendarTakes_PutsExactlyThatEventOnTheCalendar()
    {
        // Arrange
        var source = StoredEmailId.Create(new Guid("55555555-5555-5555-5555-555555555555"));
        var act = new AgentEventScheduling(PresentationText.Create("Renewal call"), Start, Start.AddHours(1), IsAllDay: false, source);

        // Act
        var carriedOut = await this.Performer().PerformAsync(act, "agent@proposal#4", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(carriedOut);
        var held = Assert.Single(await this.calendar.ReadRangeAsync(
            SyntheticUser.Deployment,
            CalendarEventQuery.Create(Start.AddDays(-1), Start.AddDays(1), origin: null, count: null),
            TestContext.Current.CancellationToken));
        Assert.Equal(
            ("Renewal call", Start, (DateTimeOffset?)Start.AddHours(1), CalendarEventOrigin.Asserted, (StoredEmailId?)source),
            (held.Title.Value, held.Start, held.End, held.Origin, held.SourceMessage));
    }

    /// <summary>A span the calendar refuses is the act not being carried out, which the proposal then ends as.</summary>
    [Fact]
    public async Task PerformAsync_AnEventTheCalendarRefuses_ReportsItNotCarriedOut()
    {
        // Arrange
        var act = new AgentEventScheduling(PresentationText.Create("Renewal call"), Start, Start, IsAllDay: false, SourceMessage: null);

        // Act
        var carriedOut = await this.Performer().PerformAsync(act, "agent@proposal#4", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(carriedOut);
    }

    /// <summary>An accepted task reaches the list exactly as it was proposed, as one the person owes and announced by nothing.</summary>
    [Fact]
    public async Task PerformAsync_ATask_WritesExactlyThatTaskOntoTheList()
    {
        // Arrange
        var act = new AgentTaskRecording(PresentationText.Create("Send the revised schedule"), new DateOnly(2026, 9, 24), SourceMessage: null);

        // Act
        var carriedOut = await this.Performer().PerformAsync(act, "agent@proposal#4", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(carriedOut);
        var held = Assert.Single(this.tasks.Held);
        Assert.Equal(
            ("Send the revised schedule", (DateOnly?)new DateOnly(2026, 9, 24), PersonalTaskOrigin.Asserted),
            (held.Title, held.DueOn, held.Origin));
    }

    /// <summary>A message the person asked to leave later is queued held until then, in the zone it was proposed in.</summary>
    [Fact]
    public async Task PerformAsync_AMessageProposedToLeaveLater_QueuesItHeldUntilThatTime()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var act = (AgentMessageSending)AgentConversationExample.Act() with { DueAt = new AgentSendTime(Start, "Europe/Warsaw") };

        // Act
        var carriedOut = await this.SendingPerformer(outgoingEmails).PerformAsync(act, "agent@proposal#4", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(carriedOut);
        Assert.Equal(ZonedInstant.Restore(Start, "Europe/Warsaw"), Assert.Single(outgoingEmails.OpenRequests).DueAt);
    }

    /// <summary>A message proposed with no time leaves as soon as it is accepted.</summary>
    [Fact]
    public async Task PerformAsync_AMessageProposedWithNoTime_QueuesItToLeaveNow()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();

        // Act
        var carriedOut = await this.SendingPerformer(outgoingEmails).PerformAsync(AgentConversationExample.Act(), "agent@proposal#4", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(carriedOut);
        Assert.Null(Assert.Single(outgoingEmails.OpenRequests).DueAt);
    }

    /// <summary>A time that passed while the proposal waited ends it not carried out, before any draft is saved for a send that cannot happen.</summary>
    [Fact]
    public async Task PerformAsync_AMessageWhoseTimeHasPassed_ReportsItNotCarriedOutAndSavesNoDraft()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var act = (AgentMessageSending)AgentConversationExample.Act() with { DueAt = new AgentSendTime(this.clock.GetUtcNow(), "Europe/Warsaw") };

        // Act
        var carriedOut = await this.SendingPerformer(outgoingEmails).PerformAsync(act, "agent@proposal#4", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(carriedOut);
        Assert.Empty(outgoingEmails.OpenRequests);
    }

    private AgentActPerformer Performer(AuthoredMailDrafting? drafting = null, UserMailDrafts? drafts = null)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.CalendarWrite, MailFathomPermission.TasksWrite);

        return new AgentActPerformer(
            drafting ?? new AuthoredMailDrafting(null!, null!, null!, null!, null!),
            new AuthoredResponseDrafting(null!, null!, null!, null!),
            drafts ?? new UserMailDrafts(null!, null!, null!, null!),
            new OwnCalendar(authorization, this.calendar, this.CommittingPolicy(), this.clock, ClientSignalPublishers.ReachingNobody),
            new OwnTasks(authorization, this.tasks, this.clock, ClientSignalPublishers.ReachingNobody),
            this.clock);
    }

    /// <summary>Builds a performer whose mail side is the real drafting, book, and outbox, so a send is read where it was queued.</summary>
    private AgentActPerformer SendingPerformer(InMemoryOutgoingEmailStore outgoingEmails)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend);
        var account = MailAccountId.Create(SentAccount);
        var catalogs = AssignedMailAccountCatalogs.For(authorization, SyntheticServedAccount.Of(account));
        var harness = new MailDraftHarness(
            this.clock,
            outgoingEmails,
            MailOutboxSettings.Create(
                maxDeliveriesPerPass: 10,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(7),
                maxAttempts: 5,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(8)));
        harness.MapDraftsFolder(account);

        var outbox = new MailOutbox(
            outgoingEmails,
            ContentStores.Substituted(),
            this.CommittingPolicy(),
            new MailOutboxSignal(capacity: 8),
            Substitute.For<IJobStore>(),
            Substitute.For<IOutboxOperationStore>(),
            authorization,
            OutgoingMailGovernors.Permitting(),
            OutgoingMailScreenings.Inactive(),
            OrganizationMailSigners.SigningNothing(),
            this.clock,
            ClientSignalPublishers.ReachingNobody);
        var promotion = new MailDraftPromotion(
            harness.Drafts,
            harness.Contents,
            outbox,
            outgoingEmails,
            this.CommittingPolicy(),
            new OutgoingEmailBounds
            {
                MaxRecipientCount = 16,
                MaxBodyCharacters = 100_000,
                MaxAttachmentCount = 4,
                MaxAttachmentBytes = 1_000_000,
                MaxMessageBytes = 1_000_000,
            },
            AuthoredSendGovernors.Permitting(),
            authorization,
            this.clock);

        return this.Performer(
            new AuthoredMailDrafting(
                catalogs,
                new NamedRecipientResolver(new InMemoryContactBookStore(), ContactBookOwnerships.For(authorization)),
                ComposingAuthoredEmails.ThatComposesDrafts(Encoding.ASCII.GetBytes("Subject: The quote\r\n\r\nThank you.")),
                harness.Book,
                authorization),
            new UserMailDrafts(catalogs, harness.Book, promotion, authorization));
    }

    private OptimisticConcurrencyRetryPolicy CommittingPolicy()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.clock);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
