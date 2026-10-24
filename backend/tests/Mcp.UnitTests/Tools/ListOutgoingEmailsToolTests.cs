// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Scheduling;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Outgoing;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what <c>list_outgoing_emails</c> owns: reading its arguments, publishing a page, and refusing what the use case refuses.</summary>
/// <remarks>
/// The tool calls the real <see cref="UserOutbox" />, so which grants it needs and which accounts it reaches are
/// decided where they are decided in production; what the substitutes replace is storage below it.
/// </remarks>
public sealed class ListOutgoingEmailsToolTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LeavesAt = new(2026, 10, 25, 7, 30, 0, TimeSpan.Zero);

    private static readonly OutgoingEmailId Send =
        OutgoingEmailId.Create(Guid.Parse("0198f0a0-3333-7000-8000-000000000003"));

    /// <summary>Every part of an entry is the published contract, so each is asserted rather than the page's size alone.</summary>
    [Fact]
    public async Task ListOutgoingEmailsAsync_ACallerThatMayReadAndSend_PublishesEachEntryWithItsRecipientsSubjectAndFailure()
    {
        // Arrange
        var operations = OperationsAnswering(new AccountOutboxPage([RefusedEntry()], NextCursor: null));
        var tool = ToolOver(operations, MailFathomPermission.MailRead, MailFathomPermission.MailSend);

        // Act
        var result = await tool.ListOutgoingEmailsAsync(
            "work",
            OutboxStage.Refused,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var listed = Assert.Single(result.OutgoingEmails);
        Assert.Equal(
            (Send.ToString(), "work", SendEmailState.Refused, false, "Quarterly figures", Recorded, (DateTimeOffset?)LeavesAt, 3, "28009"),
            (listed.OutgoingEmailId, listed.AccountId, listed.State, listed.IsBeingTransmitted, listed.Subject, listed.QueuedAt, listed.SendAt, listed.AttemptCount, listed.FailureCode));
        Assert.Equal(["anna@example.test"], listed.Recipients);
        Assert.Equal(4, listed.OtherRecipientCount);
        Assert.False(string.IsNullOrWhiteSpace(listed.Failure));
        Assert.Null(result.NextCursor);
        await operations.Received(1).ReadAccountOutboxAsync(
            Arg.Is<OutboxQuery>(query => query.AccountId == MailAccountId.Create("work") && query.Stage == OutgoingEmailStage.Refused),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A send an attempt holds is the one a caller must not expect to withdraw, so the listing says so rather than only naming its stage.</summary>
    [Fact]
    public async Task ListOutgoingEmailsAsync_ASendAnAttemptHolds_PublishesItAsBeingTransmitted()
    {
        // Arrange
        var held = RefusedEntry() with { Stage = OutgoingEmailStage.TransmissionBegun, IsHeldByAttempt = true };
        var operations = OperationsAnswering(new AccountOutboxPage([held], NextCursor: null));
        var tool = ToolOver(operations, MailFathomPermission.MailRead, MailFathomPermission.MailSend);

        // Act
        var result = await tool.ListOutgoingEmailsAsync(
            "work",
            OutboxStage.Sending,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var listed = Assert.Single(result.OutgoingEmails);
        Assert.Equal((SendEmailState.Sending, true), (listed.State, listed.IsBeingTransmitted));
    }

    /// <summary>The cursor a page answers with is the one a later call presents, so it is published in the form the tool reads back.</summary>
    [Fact]
    public async Task ListOutgoingEmailsAsync_APageWithMoreAfterIt_PublishesACursorTheNextCallPresents()
    {
        // Arrange
        var operations = OperationsAnswering(new AccountOutboxPage([RefusedEntry()], CursorOverTheWholeOutbox()));
        var tool = ToolOver(operations, MailFathomPermission.MailRead, MailFathomPermission.MailSend);

        // Act
        var first = await tool.ListOutgoingEmailsAsync("work", cancellationToken: TestContext.Current.CancellationToken);
        await tool.ListOutgoingEmailsAsync("work", cursor: first.NextCursor, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CursorOverTheWholeOutbox().Encode(), first.NextCursor);
        await operations.Received(1).ReadAccountOutboxAsync(
            Arg.Is<OutboxQuery>(query => query.Cursor == CursorOverTheWholeOutbox()),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A cursor issued for the whole outbox cannot continue a narrower listing, which would otherwise skip or repeat entries.</summary>
    [Fact]
    public async Task ListOutgoingEmailsAsync_ACursorIssuedForAnotherStage_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var tool = ToolOver(operations, MailFathomPermission.MailRead, MailFathomPermission.MailSend);

        // Act
        await Assert.ThrowsAsync<MailboxQueryCursorFilterMismatchException>(
            () => tool.ListOutgoingEmailsAsync(
                "work",
                OutboxStage.Refused,
                cursor: CursorOverTheWholeOutbox().Encode(),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        await operations.DidNotReceive().ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A page names recipients and subjects, so a credential that may only send is refused by the use case and nothing is read.</summary>
    [Fact]
    public async Task ListOutgoingEmailsAsync_ACallerThatMayOnlySend_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var tool = ToolOver(operations, MailFathomPermission.MailSend);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => tool.ListOutgoingEmailsAsync("work", cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.PrincipalNotAuthorized, refusal.ErrorCode);
        await operations.DidNotReceive().ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An account the caller's user is not assigned is refused exactly as one nobody configured.</summary>
    [Fact]
    public async Task ListOutgoingEmailsAsync_AnAccountTheUserIsNotAssigned_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var tool = ToolOver(operations, MailFathomPermission.MailRead, MailFathomPermission.MailSend);

        // Act
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => tool.ListOutgoingEmailsAsync("private", cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        await operations.DidNotReceive().ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wo\u0001rk")]
    public async Task ListOutgoingEmailsAsync_TextNoAccountCouldBeNamedBy_IsRefusedAsAnUnusableFilter(string account)
    {
        // Arrange
        var tool = ToolOver(Substitute.For<IOutboxOperationStore>(), MailFathomPermission.MailRead, MailFathomPermission.MailSend);

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.ListOutgoingEmailsAsync(account, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("account", refusal.FilterName);
    }

    /// <summary>A cursor nobody issued is the caller's own mistake, refused before anything is read rather than read from the start.</summary>
    [Fact]
    public async Task ListOutgoingEmailsAsync_ACursorThisToolDidNotIssue_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var tool = ToolOver(operations, MailFathomPermission.MailRead, MailFathomPermission.MailSend);

        // Act
        await Assert.ThrowsAsync<MailboxQueryCursorMalformedException>(
            () => tool.ListOutgoingEmailsAsync("work", cursor: "not-a-cursor", cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        await operations.DidNotReceive().ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(OutboxQuery.MaximumPageSize + 1)]
    public async Task ListOutgoingEmailsAsync_APageSizeOutsideTheRange_IsRefusedRatherThanClamped(int pageSize)
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();
        var tool = ToolOver(operations, MailFathomPermission.MailRead, MailFathomPermission.MailSend);

        // Act
        await Assert.ThrowsAsync<MailboxQueryPageSizeOutOfRangeException>(
            () => tool.ListOutgoingEmailsAsync("work", pageSize: pageSize, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        await operations.DidNotReceive().ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData((int)OutboxStage.Queued, OutgoingEmailStage.Recorded)]
    [InlineData((int)OutboxStage.Sending, OutgoingEmailStage.TransmissionBegun)]
    [InlineData((int)OutboxStage.Refused, OutgoingEmailStage.Refused)]
    public void Stored_EachPublishedStage_NamesTheStageTheOutboxHoldsItAt(int published, OutgoingEmailStage stored)
    {
        // Act
        var mapped = OutboxStageMapping.Stored((OutboxStage)published);

        // Assert
        Assert.Equal(stored, mapped);
        Assert.Contains(mapped, OutgoingEmailRecord.OutboxStages);
    }

    private static ListOutgoingEmailsTool ToolOver(IOutboxOperationStore operations, params MailFathomPermission[] granted)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(granted);

        return new ListOutgoingEmailsTool(UserOutboxes.Over(new StubMailAccountCatalog("work"), authorization, operations: operations));
    }

    private static IOutboxOperationStore OperationsAnswering(AccountOutboxPage page)
    {
        var operations = Substitute.For<IOutboxOperationStore>();
        operations
            .ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>())
            .Returns(page);

        return operations;
    }

    private static OutboxCursor CursorOverTheWholeOutbox() => OutboxCursor.After(
        Recorded,
        Send,
        OutboxQuery.Create(MailAccountId.Create("work"), null, null, null).Query!.FilterFingerprint);

    private static AccountOutboxEntry RefusedEntry()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        return new AccountOutboxEntry
        {
            OutgoingEmailId = Send,
            AccountId = MailAccountId.Create("work"),
            Stage = OutgoingEmailStage.Refused,
            IsHeldByAttempt = false,
            NamedRecipients = [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)],
            OtherRecipientCount = 4,
            Subject = "Quarterly figures",
            RecordedAt = Recorded,
            DueAt = ZonedInstant.At(LeavesAt),
            AttemptCount = 3,
            LastFailure = MailFathomErrorCode.OutgoingEmailRefused,
            LastReplyCode = 550,
        };
    }
}
