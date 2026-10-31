// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts;
using MailFathom.Application.Presence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using Microsoft.AspNetCore.SignalR;

namespace MailFathom.Host.Signals;

/// <summary>Delivers a signal to whichever clients of the people it concerns are running.</summary>
/// <remarks>
/// <para>
/// The one channel registered today. It reaches a client that has a connection open — the web head, the desktop head,
/// and the Android head while it is in the foreground — and reaches nothing at all when none of them has. That is the
/// port's whole contract rather than a shortcoming: a client with no connection catches up on its own interval, and a
/// closed Android application is a second channel's problem rather than this one's.
/// </para>
/// <para>
/// It is registered only where the client surface is served, so a deployment answering an agent alone registers no
/// channel and the publisher folds nothing.
/// </para>
/// </remarks>
internal sealed partial class SignalRClientSignalChannel : IClientSignalChannel
{
    private readonly IHubContext<ClientSignalHub> hub;
    private readonly IMailAccountAssignments assignments;
    private readonly IPresenceStore organizations;
    private readonly ILogger<SignalRClientSignalChannel> logger;

    /// <summary>Initializes the channel over the hub it publishes through.</summary>
    /// <param name="hub">Addresses one user's connections by their group.</param>
    /// <param name="assignments">Answers who is assigned the mailbox a signal names, read when the signal is delivered.</param>
    /// <param name="organizations">Answers who belongs to the organization a signal names, read when the signal is delivered.</param>
    /// <param name="logger">Records a signal that could not be delivered, in kinds and counts rather than in anything about mail.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public SignalRClientSignalChannel(
        IHubContext<ClientSignalHub> hub,
        IMailAccountAssignments assignments,
        IPresenceStore organizations,
        ILogger<SignalRClientSignalChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(organizations);
        ArgumentNullException.ThrowIfNull(logger);

        this.hub = hub;
        this.assignments = assignments;
        this.organizations = organizations;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A failure is logged and swallowed rather than propagated, because the work every signal describes is already
    /// committed and the publisher above treats delivery as an optimization. What is logged is the kind alone: an
    /// account alias, a folder alias, and a stored identity are MailFathom's own names and still say which mailbox was
    /// moving, and a log line is a wider audience than the connection this was for.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A signal that could not be delivered must not fail the work that raised it; the client's own interval closes the gap and the failure is reported here rather than thrown.")]
    public async Task PublishAsync(ClientSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);

        try
        {
            var payload = ClientSignalPayload.For(signal);

            foreach (var recipient in await this.ReadRecipientsAsync(signal, cancellationToken))
            {
                await this.hub.Clients
                    .Group(ClientSignalHub.GroupOf(recipient))
                    .SendAsync(ClientSignalHub.SignalMethod, payload, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            this.LogSignalUndelivered(exception, signal.Kind.Name);
        }
    }

    /// <summary>Resolves whose connections one signal reaches: the assigned users where it names a mailbox, and the members where it names an organization.</summary>
    /// <remarks>
    /// A signal naming a user is that person's and reaches them alone. One naming an account reaches everybody
    /// assigned that account, and one naming an organization every person in it, each resolved here rather than where
    /// the signal was composed: an assignment or a membership made between the two would otherwise deliver to the set
    /// as it stood when the write happened. A mailbox assigned to nobody reaches nobody, which is what an empty answer
    /// has to mean everywhere the assignment relation is read.
    /// </remarks>
    private async Task<IReadOnlyList<UserId>> ReadRecipientsAsync(ClientSignal signal, CancellationToken cancellationToken)
    {
        if (signal.User is { } named)
        {
            return [named];
        }

        if (signal.Account is { } account)
        {
            return await this.assignments.ReadUsersAssignedToAsync(account, cancellationToken);
        }

        if (signal.Organization is { } organization)
        {
            return await this.organizations.ReadOrganizationUsersAsync(organization, cancellationToken);
        }

        return [];
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "A {SignalKind} signal could not be delivered over the client hub; the client's own interval will close the gap.")]
    private partial void LogSignalUndelivered(Exception exception, string signalKind);
}
