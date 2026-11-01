// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Receipts;
using MailFathom.Application.Persistence;
using MailFathom.Application.Preferences;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Receipts;

/// <summary>Covers when opening a message records its reading for a colleague who sent it, and when it records nothing.</summary>
public sealed class OpenedMailReceiptsTests
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 11, 1, 10, 15, 0, TimeSpan.Zero);

    private static readonly StoredEmailId[] Opened = [StoredEmailId.Create(Guid.Parse("0192a1b2-0000-7000-8000-0000000000e1"))];

    private readonly IReceiptReportStore store = Substitute.For<IReceiptReportStore>();

    private readonly IClientPreferencesStore preferences = Substitute.For<IClientPreferencesStore>();

    /// <summary>A person who lets opening mark mail read reports their reading of what they opened, at the moment they opened it.</summary>
    [Fact]
    public async Task RecordAsync_MarkReadOnOpenOn_RecordsTheOpenedMessagesAsRead()
    {
        // Arrange
        this.preferences.ReadAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>())
            .Returns(ClientPreferences.Unset with { MarkReadOnOpen = true });

        // Act
        await this.Receipts().RecordAsync(Opened, TestContext.Current.CancellationToken);

        // Assert
        await this.store.Received(1).RecordOpenedAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<IReadOnlyCollection<StoredEmailId>>(opened => opened.SequenceEqual(Opened)),
            OpenedAt,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A person who never said anything is under the default, which marks mail read on opening and so reports it.</summary>
    [Fact]
    public async Task RecordAsync_NothingStored_RecordsUnderTheDefault()
    {
        // Act
        await this.Receipts().RecordAsync(Opened, TestContext.Current.CancellationToken);

        // Assert
        await this.store.Received(1).RecordOpenedAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<IReadOnlyCollection<StoredEmailId>>(),
            OpenedAt,
            Arg.Any<CancellationToken>());
    }

    /// <summary>Turning <c>MarkReadOnOpen</c> off is the person's switch for their reading, whatever client claimed an opening.</summary>
    [Fact]
    public async Task RecordAsync_MarkReadOnOpenOff_RecordsNothing()
    {
        // Arrange
        this.preferences.ReadAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>())
            .Returns(ClientPreferences.Unset with { MarkReadOnOpen = false });

        // Act
        await this.Receipts().RecordAsync(Opened, TestContext.Current.CancellationToken);

        // Assert
        await this.store.DidNotReceiveWithAnyArgs().RecordOpenedAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>An opening that marked nothing read, because every change in it was refused, has nothing to record and reads nothing.</summary>
    [Fact]
    public async Task RecordAsync_NothingOpened_ReadsAndRecordsNothing()
    {
        // Act
        await this.Receipts().RecordAsync([], TestContext.Current.CancellationToken);

        // Assert
        await this.preferences.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
        await this.store.DidNotReceiveWithAnyArgs().RecordOpenedAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    private OpenedMailReceipts Receipts()
    {
        var clock = new FakeTimeProvider(OpenedAt);
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new OpenedMailReceipts(
            this.store,
            this.preferences,
            AccessAuthorizations.ForCallerGranted(),
            new OptimisticConcurrencyRetryPolicy(sessions, new PersistenceConcurrencyOptions(), clock),
            clock);
    }
}
