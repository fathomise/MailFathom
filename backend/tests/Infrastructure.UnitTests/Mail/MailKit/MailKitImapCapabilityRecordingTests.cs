// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Data.Common;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit;

/// <summary>Covers that every authenticated IMAP connection records what its server advertised, reconnections included.</summary>
public sealed class MailKitImapCapabilityRecordingTests
{
    /// <summary>How far virtual time moves per step while a pipeline waits out its backoff.</summary>
    private static readonly TimeSpan BackoffAdvanceStep = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task OpenReadOnlyAsync_AServerAdvertisingUidPlusAndImap4Rev2_RecordsBothForTheAccount()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var capabilityStore = new RecordingMailboxServerCapabilityStore();
        var client = AuthenticatingClient(ImapCapabilities.UidPlus | ImapCapabilities.IMAP4rev2 | ImapCapabilities.Idle);

        // Act
        await using var session = await OpenSessionAsync(resilience, capabilityStore, ConnectionSequence(client));

        // Assert
        Assert.Equal(
            [(PrimaryAccount, MailboxServerCapabilities.UidPlus | MailboxServerCapabilities.Imap4Rev2)],
            capabilityStore.Recordings);
    }

    /// <summary>A server that stopped advertising a capability has to replace the record that said it did.</summary>
    [Fact]
    public async Task OpenReadOnlyAsync_AServerAdvertisingNeither_RecordsThatItAdvertisesNothing()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var capabilityStore = new RecordingMailboxServerCapabilityStore();
        var client = AuthenticatingClient(ImapCapabilities.Move | ImapCapabilities.Idle);

        // Act
        await using var session = await OpenSessionAsync(resilience, capabilityStore, ConnectionSequence(client));

        // Assert
        Assert.Equal([(PrimaryAccount, MailboxServerCapabilities.None)], capabilityStore.Recordings);
    }

    /// <summary>A reconnection can land on a server upgraded behind the same address, so it records what that one advertises.</summary>
    [Fact]
    public async Task FetchEmailContentWithoutSettingSeenAsync_AReconnectionToAnUpgradedServer_RecordsTheNewAdvertisement()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var capabilityStore = new RecordingMailboxServerCapabilityStore();
        var droppedClient = AuthenticatingClient(ImapCapabilities.None);
        var droppedFolder = CreateSelectedFolder();
        droppedFolder.GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>())
            .Returns<Stream>(_ => throw droppedClient.DropConnection(new IOException("the server closed the connection")));
        droppedClient.Folder = droppedFolder;
        var upgradedClient = AuthenticatingClient(ImapCapabilities.UidPlus);
        var upgradedFolder = CreateSelectedFolder();
        upgradedFolder.GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream("Subject: Subject\r\n\r\nBody"u8.ToArray()));
        upgradedClient.Folder = upgradedFolder;
        await using var session = await OpenSessionAsync(
            resilience,
            capabilityStore,
            ConnectionSequence(droppedClient, upgradedClient));

        // Act
        var fetch = await resilience.CompleteOnVirtualTimeAsync(
            session.FetchEmailContentWithoutSettingSeenAsync(CreateOccurrenceId(10), 1024, CancellationToken.None),
            BackoffAdvanceStep);

        // Assert
        Assert.Equal(RemoteEmailContentFetchOutcome.Retrieved, fetch.Outcome);
        Assert.Equal(
            [
                (PrimaryAccount, MailboxServerCapabilities.None),
                (PrimaryAccount, MailboxServerCapabilities.UidPlus),
            ],
            capabilityStore.Recordings);
    }

    /// <summary>A connection whose recording failed is given up, so nobody takes it as established and skips the recording.</summary>
    [Fact]
    public async Task OpenReadOnlyAsync_TheRecordingIsRefused_FailsAndGivesTheConnectionUp()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var capabilityStore = new RecordingMailboxServerCapabilityStore();
        capabilityStore.RefusedRecordings.Enqueue(new RefusedRecordingException());
        var client = AuthenticatingClient(ImapCapabilities.UidPlus);

        // Act
        await Assert.ThrowsAsync<RefusedRecordingException>(
            () => OpenSessionAsync(resilience, capabilityStore, ConnectionSequence(client)));

        // Assert
        Assert.Equal(1, client.DisposeCount);
        Assert.Empty(capabilityStore.Recordings);
    }

    /// <summary>
    /// A database failure is not a transport failure, so the read's pipeline does not repeat it; what matters is that the
    /// connection the refused recording was made for is not kept, so the next read establishes one and records.
    /// </summary>
    [Fact]
    public async Task FetchEmailContentWithoutSettingSeenAsync_AReconnectionWhoseRecordingIsRefused_RecordsOnTheNextEstablishment()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var capabilityStore = new RecordingMailboxServerCapabilityStore();
        var droppedClient = AuthenticatingClient(ImapCapabilities.None);
        var droppedFolder = CreateSelectedFolder();
        droppedFolder.GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>())
            .Returns<Stream>(_ => throw droppedClient.DropConnection(new IOException("the server closed the connection")));
        droppedClient.Folder = droppedFolder;
        var refusedClient = AuthenticatingClient(ImapCapabilities.UidPlus);
        var recordedClient = AuthenticatingClient(ImapCapabilities.UidPlus);
        var recordedFolder = CreateSelectedFolder();
        recordedFolder.GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream("Subject: Subject\r\n\r\nBody"u8.ToArray()));
        recordedClient.Folder = recordedFolder;
        await using var session = await OpenSessionAsync(
            resilience,
            capabilityStore,
            ConnectionSequence(droppedClient, refusedClient, recordedClient));
        capabilityStore.RefusedRecordings.Enqueue(new RefusedRecordingException());

        // Act
        await Assert.ThrowsAsync<RefusedRecordingException>(() => resilience.CompleteOnVirtualTimeAsync(
            session.FetchEmailContentWithoutSettingSeenAsync(CreateOccurrenceId(10), 1024, CancellationToken.None),
            BackoffAdvanceStep));
        var fetch = await resilience.CompleteOnVirtualTimeAsync(
            session.FetchEmailContentWithoutSettingSeenAsync(CreateOccurrenceId(10), 1024, CancellationToken.None),
            BackoffAdvanceStep);

        // Assert
        Assert.Equal(RemoteEmailContentFetchOutcome.Retrieved, fetch.Outcome);
        Assert.Equal(1, refusedClient.DisposeCount);
        Assert.Equal(
            [
                (PrimaryAccount, MailboxServerCapabilities.None),
                (PrimaryAccount, MailboxServerCapabilities.UidPlus),
            ],
            capabilityStore.Recordings);
    }

    private static FakeImapClient AuthenticatingClient(ImapCapabilities advertised)
    {
        var client = new FakeImapClient { Capabilities = advertised, Folder = CreateSelectedFolder() };
        client.AuthenticationMechanisms.Add("PLAIN");

        return client;
    }

    private static Task<IMailboxSession> OpenSessionAsync(
        OutboundResilienceTestHost resilience,
        RecordingMailboxServerCapabilityStore capabilityStore,
        Func<IImapClient> clientFactory) =>
        CreateFactory(
                resilience,
                clientFactory,
                CreateSettingsProvider(),
                new UnusedMailAccessTokenSource(),
                capabilityStore)
            .OpenReadOnlyAsync(PrimaryAccount, InboxFolder, TlsOnConnectWithPlainPolicy, CancellationToken.None);

    /// <summary>Stands for the database refusing the write, which is what a provider raises as a <see cref="DbException" />.</summary>
    private sealed class RefusedRecordingException() : DbException("the database refused the recording");
}
