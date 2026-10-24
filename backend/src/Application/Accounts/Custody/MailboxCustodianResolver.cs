// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts.Custody;

/// <summary>Chooses which custodian carries an act on one account's mailbox, from the account's custody phase.</summary>
/// <remarks>
/// <para>
/// This is the only reader of the phase for an act, and its <c>switch</c> is the only one over
/// <see cref="MailAccountCustodyPhase" /> outside the background work that exists because of a mode. A new phase is a
/// new arm here and a new implementation of <see cref="IMailboxCustodian" />, and nothing else. It is not a compiler
/// error: the <c>switch</c> ends in an arm that throws, so a phase added without its arm compiles and fails on the first
/// act on an account in it, rather than being carried by a custodian chosen for another phase.
/// </para>
/// <para>
/// An account no custody was ever recorded for is mirrored: its row is written by its first synchronization, and a row
/// starts mirrored. The mirrored custodian answers its folder reads and acts as an account the deployment does not hold
/// yet, exactly as it would have been answered before it existed.
/// </para>
/// <para>
/// The custodians are taken lazily because they compose the collaborators that already do each act, and several of
/// those resolve a destination or file a copy through this resolver in turn. Each is built once per scope, on the first
/// act that needs it.
/// </para>
/// </remarks>
public sealed class MailboxCustodianResolver
{
    private readonly IMailAccountCustodyStore custody;
    private readonly Lazy<MirroredMailboxCustodian> mirrored;
    private readonly Lazy<HeldMailboxCustodian> held;
    private readonly Lazy<RestoringMailboxCustodian> restoring;

    /// <summary>Initializes the resolver over the store the phase is read from and the custodian of each phase.</summary>
    /// <param name="custody">Reads the account's custody phase.</param>
    /// <param name="mirrored">Carries the acts of an account whose source is the truth.</param>
    /// <param name="held">Carries the acts of an account whose mailbox MailFathom holds alone.</param>
    /// <param name="restoring">Carries the acts of an account whose mailbox is being restored to its source.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailboxCustodianResolver(
        IMailAccountCustodyStore custody,
        Lazy<MirroredMailboxCustodian> mirrored,
        Lazy<HeldMailboxCustodian> held,
        Lazy<RestoringMailboxCustodian> restoring)
    {
        ArgumentNullException.ThrowIfNull(custody);
        ArgumentNullException.ThrowIfNull(mirrored);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(restoring);

        this.custody = custody;
        this.mirrored = mirrored;
        this.held = held;
        this.restoring = restoring;
    }

    /// <summary>Chooses the custodian for an act that runs before, or outside, any transaction.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The custodian.</returns>
    public async Task<IMailboxCustodian> ResolveAsync(MailAccountId account, CancellationToken cancellationToken) =>
        this.Of(await this.custody.ReadAsync(account, cancellationToken));

    /// <summary>Chooses the custodian inside the transaction an act commits in.</summary>
    /// <param name="session">The transaction.</param>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The custodian.</returns>
    /// <remarks>
    /// Read in the caller's transaction, so a switch of custody cannot be judged differently by two acts in one request,
    /// and an act commits together with the phase it was judged under.
    /// </remarks>
    public async Task<IMailboxCustodian> ResolveAsync(
        IPersistenceSession session,
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.Of(await this.custody.ReadAsync(session, account, cancellationToken));

    private IMailboxCustodian Of(MailAccountCustodyState? state) => state?.Phase switch
    {
        null or MailAccountCustodyPhase.Mirrored => this.mirrored.Value,
        MailAccountCustodyPhase.Held => this.held.Value,
        MailAccountCustodyPhase.Restoring => this.restoring.Value,
        _ => throw new InvalidOperationException($"No custodian carries the acts of custody phase {state.Phase}."),
    };
}
