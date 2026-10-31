// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Application.Presence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Signals;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers which connections a signal is addressed to, what is sent to them, and what a failed send does to the run that raised it.</summary>
public sealed class SignalRClientSignalChannelTests
{
    private static readonly MailAccountId Account =
        MailAccountId.Create("work");

    private static readonly MailFolderId Inbox = MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-000000000007"));

    /// <summary>A signal reaches the assigned user's group, under the one method name a client keys its handler by, as the payload rather than as itself.</summary>
    [Fact]
    public async Task PublishAsync_ASignal_SendsItsRenderingToTheAssignedUsersGroupUnderTheOnePublishedMethod()
    {
        // Arrange
        var group = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(group);

        object?[]? sent = null;
        var method = string.Empty;
        group.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                method = call.Arg<string>();
                sent = call.Arg<object?[]>();

                return Task.CompletedTask;
            });

        var channel = ChannelOver(clients);

        // Act
        await channel.PublishAsync(ClientSignal.MailArrived(Account, Inbox, newEmailCount: 3), CancellationToken.None);

        // Assert
        clients.Received(1).Group(ClientSignalHub.GroupOf(SyntheticUser.Deployment));
        Assert.Equal(ClientSignalHub.SignalMethod, method);
        Assert.NotNull(sent);
        var payload = Assert.IsType<ClientSignalPayload>(Assert.Single(sent));
        Assert.Equal(ClientSignalKind.MailArrived.Name, payload.Kind);
        Assert.Equal(Account.Value, payload.Account);
        Assert.Equal(Inbox.ToString(), payload.Folder);
        Assert.Equal(3, payload.Count);
    }

    /// <summary>A send that failed stops here, because the work every signal describes is already committed.</summary>
    [Fact]
    public async Task PublishAsync_AHubThatCannotSend_AbsorbsTheFailureRatherThanFailingTheRunThatRaisedIt()
    {
        // Arrange
        var group = Substitute.For<IClientProxy>();
        group.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("This hub cannot send."));
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(group);
        var channel = ChannelOver(clients);

        // Act
        await channel.PublishAsync(ClientSignal.FoldersChanged(Account), CancellationToken.None);

        // Assert
        await group.Received(1).SendCoreAsync(
            ClientSignalHub.SignalMethod,
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Builds the channel over a deployment assigning the mailbox to whoever the test named.</summary>
    /// <remarks>
    /// The scope factory is a real one rather than a substitute, because the channel resolves the assignment relation
    /// per signal from a scope of its own — that is what makes an assignment change between two signals visible to
    /// the second — and a substituted factory would prove the wiring against itself.
    /// </remarks>
    /// <summary>
    /// One mailbox assigned to two people wakes both of their clients. The recipients are resolved from the
    /// assignment relation at the moment the signal is published rather than carried on the signal, so a mailbox
    /// somebody was assigned after a run started still reaches them.
    /// </summary>
    [Fact]
    public async Task PublishAsync_ASignalNamingAMailboxAssignedToTwoUsers_ReachesBothTheirGroups()
    {
        // Arrange
        var group = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(group);
        var channel = ChannelOver(
            clients,
            new StubMailAccountAssignments()
                .Assigning(SyntheticUser.Deployment, Account)
                .Assigning(SyntheticUser.Another, Account));

        // Act
        await channel.PublishAsync(ClientSignal.FoldersChanged(Account), CancellationToken.None);

        // Assert
        clients.Received(1).Group(ClientSignalHub.GroupOf(SyntheticUser.Deployment));
        clients.Received(1).Group(ClientSignalHub.GroupOf(SyntheticUser.Another));
    }

    /// <summary>A mailbox assigned to nobody reaches nobody, which is what an empty assignment answer has to mean.</summary>
    [Fact]
    public async Task PublishAsync_ASignalNamingAMailboxAssignedToNobody_SendsNothing()
    {
        // Arrange
        var group = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(group);
        var channel = ChannelOver(clients, new StubMailAccountAssignments());

        // Act
        await channel.PublishAsync(ClientSignal.FoldersChanged(Account), CancellationToken.None);

        // Assert
        Assert.Empty(clients.ReceivedCalls());
        Assert.Empty(group.ReceivedCalls());
    }

    /// <summary>A change to an organization's contact book reaches every member's group, read when the signal is delivered, and carries no organization on the wire.</summary>
    [Fact]
    public async Task PublishAsync_AnOrganizationsContactsChanged_ReachesEveryMembersGroup()
    {
        // Arrange
        var organization = Guid.Parse("0199c1a2-0000-7000-8000-0000000000a1");
        var group = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(group);
        var organizations = Substitute.For<IPresenceStore>();
        organizations.ReadOrganizationUsersAsync(organization, Arg.Any<CancellationToken>())
            .Returns([SyntheticUser.Deployment, SyntheticUser.Another]);
        var channel = ChannelOver(clients, organizations: organizations);

        // Act
        await channel.PublishAsync(
            ClientSignal.ContactsChanged(ContactBookHolder.OfOrganization(organization)),
            CancellationToken.None);

        // Assert
        clients.Received(1).Group(ClientSignalHub.GroupOf(SyntheticUser.Deployment));
        clients.Received(1).Group(ClientSignalHub.GroupOf(SyntheticUser.Another));
        await group.Received(2).SendCoreAsync(
            ClientSignalHub.SignalMethod,
            Arg.Is<object?[]>(sent => sent.Length == 1
                && ((ClientSignalPayload)sent[0]!).Kind == ClientSignalKind.ContactsChanged.Name
                && ((ClientSignalPayload)sent[0]!).Account == null),
            Arg.Any<CancellationToken>());
    }

    private static SignalRClientSignalChannel ChannelOver(
        IHubClients clients,
        IMailAccountAssignments? assignments = null,
        IPresenceStore? organizations = null)
    {
        var hub = Substitute.For<IHubContext<ClientSignalHub>>();
        hub.Clients.Returns(clients);

        return new SignalRClientSignalChannel(
            hub,
            assignments ?? new StubMailAccountAssignments().Assigning(SyntheticUser.Deployment, Account),
            organizations ?? Substitute.For<IPresenceStore>(),
            NullLogger<SignalRClientSignalChannel>.Instance);
    }
}
