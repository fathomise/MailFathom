// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Folders;

/// <summary>Reads the user's mailboxes and their folders as the one tree a mail screen is drawn from.</summary>
/// <remarks>
/// <para>
/// It answers in one read what a screen would otherwise assemble from three: which mailboxes there are, which folders
/// each of them has and where those sit in its hierarchy, and how current every one of them is. Splitting any of that
/// off would make drawing one tree several requests whose answers disagree with each other by the time the last one
/// arrives — which is the shape this surface exists not to have.
/// </para>
/// <para>
/// The accounts and every freshness reading are <see cref="MailAccountFreshnessReader" />'s, composed rather than
/// re-derived, so the folder tree and the mailbox list beside it cannot come to disagree about the same account. What
/// is added here is what that reading has no reason to carry: each folder's identity, the role it plays, its place in
/// the hierarchy, and how much mail is stored in it.
/// </para>
/// <para>
/// The folders are the account's folder rows, the same for every custody phase: on a mirrored account synchronization
/// keeps them as the projection of the source and gives every declared folder one, mirrored or not, and on a held
/// account they are the hierarchy people edit. So a folder somebody created on a held account, and a message moved into
/// it, are in the tree exactly as a mirrored folder and its mail are.
/// </para>
/// <para>
/// It reaches no mail server and returns no mail. Folder names, roles, counts, and instants are the whole of it, and
/// asking cannot set the remote <c>\Seen</c> flag.
/// </para>
/// </remarks>
public sealed class MailFolderDirectoryReader
{
    private readonly MailAccountFreshnessReader accountReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IStoredMailFolderReader storedFolders;
    private readonly UserOutbox outbox;

    /// <summary>Initializes the use case.</summary>
    /// <param name="accountReader">Reads the caller's accounts and how current each one and each of its folders is.</param>
    /// <param name="scopeResolver">Answers which of those accounts' folders may be reported on.</param>
    /// <param name="storedFolders">Reads each folder's place, role, and how much of it is stored.</param>
    /// <param name="outbox">Counts each account's outbox, for a caller that may read it.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailFolderDirectoryReader(
        MailAccountFreshnessReader accountReader,
        MailboxScopeResolver scopeResolver,
        IStoredMailFolderReader storedFolders,
        UserOutbox outbox)
    {
        ArgumentNullException.ThrowIfNull(accountReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(storedFolders);
        ArgumentNullException.ThrowIfNull(outbox);

        this.accountReader = accountReader;
        this.scopeResolver = scopeResolver;
        this.storedFolders = storedFolders;
        this.outbox = outbox;
    }

    /// <summary>Reads the user's mailboxes and every folder a screen may draw beneath them.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per account the caller's user is assigned, each with its folders, and whether the deployment refreshes them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" /> that is acting for a user.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The grant and the user bound are the composed reading's, taken before anything here runs, so a user assigned
    /// no account reads an empty tree and a caller without the permission is refused — the same two answers the mailbox
    /// list gives, because naming a user's folders is the same disclosure as naming their mailboxes.
    /// </remarks>
    public async Task<MailFolderDirectory> ReadAsync(CancellationToken cancellationToken)
    {
        var accounts = await this.accountReader.ReadAsync(cancellationToken);

        if (accounts.Accounts.Count is 0)
        {
            return new MailFolderDirectory(accounts.SynchronizationEnabled, []);
        }

        // The scope the composed reading resolved, asked for again rather than passed down: it is read again from the
        // account records, so it matches the folders that reading reported freshness for unless an account write
        // commits between the two reads. Junk is included for the reason the mailbox list includes it — the
        // withholding is about not returning its mail unasked, and no mail is returned here.
        var scope = await this.scopeResolver.ReadableScopeAsync([], [], JunkMailInclusion.Included, cancellationToken);

        var stored = await this.storedFolders.ReadAsync(scope, cancellationToken);
        var foldersByAccount = stored.ToLookup(static folder => folder.Account);
        var outboxes = await this.ReadOutboxCountsAsync(accounts.Accounts, cancellationToken);

        return new MailFolderDirectory(
            accounts.SynchronizationEnabled,
            [
                .. accounts.Accounts.Select(account => Describe(account, foldersByAccount[account.Account.Id]) with
                {
                    Outbox = outboxes.GetValueOrDefault(account.Account.Id),
                }),
            ]);
    }

    /// <summary>Counts each account's outbox, for a caller that may read the outbox at all.</summary>
    private async Task<IReadOnlyDictionary<MailAccountId, AccountOutboxCount>> ReadOutboxCountsAsync(
        IReadOnlyList<MailAccountFreshness> accounts,
        CancellationToken cancellationToken)
    {
        if (!this.outbox.MayReadOutbox)
        {
            return new Dictionary<MailAccountId, AccountOutboxCount>();
        }

        var counted = await this.outbox.CountAsync(
            [.. accounts.Select(static account => account.Account.Id)],
            cancellationToken);

        return counted.ToDictionary(static count => count.AccountId);
    }

    /// <summary>Describes one account's folders, ordered by their place in its hierarchy.</summary>
    /// <remarks>
    /// A folder configuration names carries the freshness the composed reading reported for its alias, and is never
    /// synchronized when that reading names none — a folder declared and not mirrored; one no declaration names is a
    /// folder MailFathom keeps itself and carries none. Ordered here rather than in SQL because the
    /// order is ordinal by contract, and a database's collation is not something MailFathom configures.
    /// </remarks>
    private static MailAccountFolders Describe(MailAccountFreshness account, IEnumerable<StoredMailFolder> folders)
    {
        var freshnessByAlias = account.Folders.ToDictionary(static folder => folder.Alias);

        return new(
            account,
            [
                .. folders
                    .OrderBy(static folder => string.Join('\u0000', folder.HierarchyLevels), StringComparer.Ordinal)
                    .ThenBy(static folder => folder.Id.Value)
                    .Select(folder => new DescribedMailFolder(
                        folder.Id,
                        folder.Alias is { } alias
                            ? freshnessByAlias.GetValueOrDefault(alias)
                                ?? new MailFolderFreshness(alias, MailSynchronizationState.NeverSynchronized, null, false)
                            : null,
                        folder.Role,
                        folder.HierarchyLevels,
                        folder.StoredEmailCount,
                        folder.UnreadEmailCount)),
            ]);
    }
}
