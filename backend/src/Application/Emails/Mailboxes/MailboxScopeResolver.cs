// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>Decides which accounts and folders a mailbox read actually runs against.</summary>
/// <remarks>
/// <para>
/// Every read model asks the same two questions of a requested scope, and both answers are access decisions rather than
/// query details, which is why they are resolved once here instead of per use case. A read model that answered either
/// one differently would publish mail through the query that got it wrong while the others stayed correct.
/// </para>
/// <para>
/// The assignment is the third thing settled here, and it is settled twice over. It reaches the resolution that runs
/// before the query — the accounts a request is resolved against are the accounts the caller's user is assigned, so a
/// mailbox nobody assigned them is refused rather than narrowed away — and it also reaches the query
/// itself, as the account term of the predicate and the column every index that predicate is planned against leads
/// with. The two are not alternatives: the resolution is what refuses without disclosing, and the term is what keeps a
/// query from reading past the accounts that survived it. No mail row names a user, so the term is the assigned
/// accounts rather than the user beside them. That is why this type reads the caller-scoped catalog and no other, for
/// both answers: a resolver holding both catalogs would be one substitution away from answering a caller out of the
/// deployment's whole account set.
/// </para>
/// </remarks>
public sealed class MailboxScopeResolver
{
    private readonly ICallerMailAccountCatalog accountCatalog;
    private readonly IMailFolderParticipationReader folderParticipation;
    private readonly IDeploymentMailFolders deploymentFolders;
    private readonly MailFolderReferenceResolver folderReferences;

    /// <summary>Initializes the resolver.</summary>
    /// <param name="accountCatalog">Answers which accounts the user this work is acting for is assigned.</param>
    /// <param name="folderParticipation">Answers whether one folder is one a reader may reach.</param>
    /// <param name="deploymentFolders">Answers which folders of the accounts in scope each kind of reader may reach, and which is each one's junk folder.</param>
    /// <param name="folderReferences">Answers whether an account in scope has a folder playing a role a request named.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxScopeResolver(
        ICallerMailAccountCatalog accountCatalog,
        IMailFolderParticipationReader folderParticipation,
        IDeploymentMailFolders deploymentFolders,
        MailFolderReferenceResolver folderReferences)
    {
        ArgumentNullException.ThrowIfNull(accountCatalog);
        ArgumentNullException.ThrowIfNull(folderParticipation);
        ArgumentNullException.ThrowIfNull(deploymentFolders);
        ArgumentNullException.ThrowIfNull(folderReferences);

        this.accountCatalog = accountCatalog;
        this.folderParticipation = folderParticipation;
        this.deploymentFolders = deploymentFolders;
        this.folderReferences = folderReferences;
    }

    /// <summary>Resolves what a request named into the scope a query runs with.</summary>
    /// <param name="reader">Who the read is answering for, which decides whether folders withheld from AI are in it.</param>
    /// <param name="accountSelectors">The text a request named accounts with, or empty for every account the caller's user is assigned.</param>
    /// <param name="folders">The folders a request named, each by its folder identifier or by role, or empty for every folder.</param>
    /// <param name="junkMail">Whether the caller asked for the account's junk folder, which defaults to it being left out. For an agent's read it decides only whether the junk folder is listed as a folder: which of its messages the read reaches is the verdict's to decide.</param>
    /// <param name="cancellationToken">Cancels the reads of which folders the accounts in scope admit.</param>
    /// <returns>The scope a query runs with.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work in hand is acting for no user, which the deployment administrator and this process's own identity both are.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when either list names more values than its limit permits.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account the caller's user is not assigned, which includes every account this deployment does not serve.</exception>
    /// <exception cref="MailFolderRoleUnmappedException">Thrown when the request names a role no account in scope maps a folder with.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="reader" /> or <paramref name="junkMail" /> is not a defined member.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="folders" /> names a folder by its alias, which no request may.</exception>
    /// <remarks>
    /// <para>
    /// An account may be named by the identifier the deployment generated for it or by the display name it is published under, and this is
    /// where the two become one identity. Resolution happens against the accounts the caller's user is assigned rather
    /// than at a protocol boundary, so text naming nothing is refused by the same rule and with the same failure as an identifier
    /// the deployment stopped serving and as one belonging to somebody else — a caller cannot learn from the refusal
    /// which of the three it was holding, so a refusal enumerates neither spellings nor other users' mailboxes.
    /// </para>
    /// <para>
    /// An account the caller's user is not assigned is refused before anything is read, rather than narrowed away by a
    /// predicate: a narrowed predicate would answer with an empty result, and an empty result tells a caller that the
    /// name they used exists. The resolved identities are what the scope is built from, so the same account named two
    /// ways is one query with one continuation cursor.
    /// </para>
    /// <para>
    /// A request that names no account is restricted to the accounts the user is assigned rather than left
    /// unrestricted. Two separate things make that necessary: removing an account from configuration leaves its stored rows in place, so an
    /// absent account predicate would keep publishing mail from an account this deployment no longer serves, and the rows
    /// of every other user are in the same table. A user assigned nothing therefore resolves to
    /// <see cref="MailboxScope.NothingReadable" /> rather than to a scope carrying an empty account list: the account
    /// containment is composed unconditionally, so an empty list admits no row, but such a scope would still carry the
    /// deployment's folder decisions and read as a scope over every mailbox's folders. Leaving early is what keeps the
    /// two apart. The resolved accounts, not the requested ones, take part in a continuation cursor's fingerprint.
    /// </para>
    /// <para>
    /// The folders a read may reach are named here, once, rather than by each read model — which is what makes "no agent
    /// and no model reaches it" a property of the system instead of a list of places somebody has to keep complete. The
    /// answer depends on who the read is for, and the caller says so rather than this type guessing: an agent's read
    /// reaches the folders visible to AI, and the person's read through the client reaches every folder MailFathom
    /// mirrors, so a folder withheld from AI stays the person's to read. It is stated as what is admitted rather than as
    /// what is withheld, because the store holds rows of folders configuration no longer declares and no list of
    /// withheld names reaches those: such a folder stays out by not being admitted, for either reader. A folder no
    /// declaration names at all — one a person made on a held account — is the other case: no setting can name it, so it
    /// is admitted with its account for either reader rather than hidden. A read that reached the mailbox some other
    /// way would bypass this, which is why the two reads that reach an email by its identifier ask the folder mapping
    /// directly rather than building a scope. Until #2330 moves them, those two read the mapping in the settings the
    /// work's own scope was prepared with while a scope reads the account's settings columns. A request's settings come
    /// from the user this replica holds, which it brings to the committed version within one convergence interval — at
    /// once on the replica that wrote it, and sooner wherever the announcement arrives — so for that long a folder a
    /// write withheld can stay readable by identifier while it is already out of every scope.
    /// </para>
    /// <para>
    /// The junk folder is withheld here too, and it is a different kind of decision from the one above: an operator did
    /// not withhold it, the default did, and a caller may ask for it back. Resolving both in one place is what keeps the
    /// override from being able to reveal a folder the operator withheld — a folder that is hidden *and* junk stays out
    /// under either answer, because the two lists narrow the query independently.
    /// </para>
    /// <para>
    /// An agent's read is answered by the spam verdict on top of all of that, and no caller can ask it back: a message
    /// whose verdict is spam is out of it in any folder, and the junk folder's mail is out of it unless the person marked
    /// the message not spam. The rule is read from the verdict when the query runs, so a message marked either way is
    /// out of, or back in, the very next read with nothing reindexed. <see cref="MailboxScope.Reader" /> carries it.
    /// </para>
    /// <para>
    /// A folder is named by the identifier MailFathom issued it or by the role it plays, never by its alias, which is
    /// configuration's name for it rather than one a caller is given. An identifier names one account's folder and is
    /// passed to the scope as it is. A role is passed as a role and read against each account's own folder rows when the
    /// query runs, because a role means a different folder on each account in scope, so one role narrows a
    /// multi-account read to each account's own folder. A role no account in scope maps is refused instead of narrowed away, for the
    /// reason an unserved account is: the caller asked for a folder that is not there, and an empty page would read as
    /// a folder holding no mail. Naming <c>role:Junk</c> is narrowing rather than asking — the withholding above still
    /// applies, so a person's read that names it and does not also ask for junk mail returns nothing, and an agent's
    /// returns only what the person marked not spam.
    /// </para>
    /// </remarks>
    public async Task<MailboxScope> ReadableScopeAsync(
        MailboxReaderKind reader,
        IReadOnlyList<MailAccountSelector> accountSelectors,
        IReadOnlyList<MailFolderReference> folders,
        JunkMailInclusion junkMail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountSelectors);
        ArgumentNullException.ThrowIfNull(folders);

        var readableFolders = ReadableFoldersFor(reader);

        if (!Enum.IsDefined(junkMail))
        {
            throw new ArgumentOutOfRangeException(
                nameof(junkMail),
                junkMail,
                "A read either reaches into the junk folder or leaves it out, and no other value names an answer.");
        }

        var assignedAccounts = await this.accountCatalog.ReadAssignedAccountsAsync(cancellationToken);

        // Counted before anything is resolved, because the count is the caller's and each resolution walks the
        // assigned accounts or that account's folders. The scope's own limits are reused rather than second ones
        // invented for the text the identities arrive as.
        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(
            accountSelectors.Count,
            MailboxScope.MaximumAccountIds,
            "accounts");
        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(
            folders.Count,
            MailboxScope.MaximumFolders,
            "folders");

        var requestedAccountIds = accountSelectors
            .Select(selector => ResolvedAccountId(selector, assignedAccounts))
            .ToArray();

        MailAccountId[] accountsInScope = requestedAccountIds.Length is 0
            ? [.. assignedAccounts.Select(static account => account.Id)]
            : requestedAccountIds;

        // A user assigned no account leaves before the folder decisions are read, because those are read for the
        // accounts in scope and an empty list of accounts has no folders to restrict a scope to. The scope that admits
        // nothing is what such a caller resolves to instead, and the predicate's unconditional account containment is
        // the second half of the same answer.
        //
        // The junk-mail answer is still recorded on it, for the reason WithJunkMail records one when no account maps a
        // junk folder: the answer is part of what a continuation cursor was issued for, and a page that reported the
        // caller's own filter back to them differently is a page whose cursor no longer matches the request.
        if (accountsInScope.Length is 0)
        {
            return MailboxScope.NothingReadable.WithJunkMail(junkMail, []).AnsweringFor(reader, []);
        }

        var resolvedScope = MailboxScope.Create(
            accountsInScope,
            [.. folders.Select(static folder => folder.Folder).OfType<MailFolderId>()],
            this.ResolvedRoles(accountsInScope, folders));

        // Read for the accounts in scope alone, because the folders of anybody else's mailbox are no part of a read
        // that cannot reach them.
        var admitted = await this.deploymentFolders.ReadAsync(
            readableFolders,
            accountsInScope,
            cancellationToken);
        var junkFolders = await this.deploymentFolders.ReadAsync(
            MailFolderSelection.Junk,
            accountsInScope,
            cancellationToken);

        return resolvedScope
            .RestrictedTo(admitted)
            .WithJunkMail(junkMail, junkFolders)
            .AnsweringFor(reader, junkFolders);
    }

    /// <summary>Gets the user this unit of work is acting for, which is what decides the posture its content is scanned under.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work in hand is acting for no user.</exception>
    /// <remarks>
    /// Published here rather than read from the catalog by each use case, so the accounts a read narrows to and the
    /// user its content is scanned under are resolved in one place. It is the user the unit of work is acting for whatever scope was
    /// resolved, which is why a use case reads it here rather than off the scope: no mail row carries a user, so a
    /// scope names accounts and nothing else, while whose posture a run's text is scanned under is still a question
    /// about the person in hand. It is also available before any scope is — what a use case needs, since a search
    /// reaches a model provider with its query text before it has a page to narrow.
    /// </remarks>
    public UserId User => this.accountCatalog.User;

    /// <summary>Reads the accounts this unit of work may reach, which is what a read that builds no scope narrows by.</summary>
    /// <param name="cancellationToken">Cancels the read of the user's assignments.</param>
    /// <returns>The accounts the user in hand is assigned and this deployment serves.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work in hand is acting for no user.</exception>
    /// <remarks>
    /// The same answer <see cref="ReadableScopeAsync" /> narrows a query with, published for the reads that reach a row by
    /// its own generated identifier and build no scope at all: a mutation record, a withdrawal, a release. A caller
    /// assigned nothing gets an empty list, and a containment test against an empty list reads nothing, which is the
    /// same fail-closed answer <see cref="MailboxScope.NothingReadable" /> is.
    /// </remarks>
    public async Task<IReadOnlyList<MailAccountId>> ReadAssignedAccountsAsync(CancellationToken cancellationToken) =>
        [.. (await this.accountCatalog.ReadAssignedAccountsAsync(cancellationToken)).Select(static account => account.Id)];

    /// <summary>Reports whether a reader may reach one email, given the mailbox it was stored from.</summary>
    /// <param name="reader">Who the read or the act is answering for.</param>
    /// <param name="accountId">The account the email was read from.</param>
    /// <param name="folderAlias">The alias of the folder the email is in, or <see langword="null" /> for a folder no declaration names, which no setting withholds.</param>
    /// <param name="cancellationToken">Cancels the read of the user's assignments.</param>
    /// <returns><see langword="true" /> when the user in hand is assigned that account and its mapping admits that folder to <paramref name="reader" />.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work in hand is acting for no user, which is a refusal rather than the <see langword="false" /> a caller with nothing readable is answered with.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="reader" /> is not a defined member.</exception>
    /// <remarks>
    /// This is <see cref="ReadableScopeAsync" /> asked about one email instead of about a query, and it exists because
    /// several reads and acts reach an email by its identifier and build no scope at all. Both questions are answered from the
    /// same catalog here, so an account this caller is not assigned cannot be reachable by identifier while it is
    /// unreachable by name. The folder is a different matter until #2330 moves this read: it asks the mapping in the
    /// settings the work's own scope was prepared with, while a scope reads the account's settings columns, so a folder a
    /// write withheld stays readable here for up to one convergence interval of the commit on every replica but the one
    /// that wrote it, while it is already out of every scope. It is a mapping being asked to admit the folder
    /// rather than a list being asked whether it names it, so an email in a folder whose alias no mapping names any more
    /// is unreadable by the same answer that withholds a mapped folder. An email in a folder with no alias at all — one a
    /// person made on a held account — is readable, because no setting can name that folder to withhold it, which is
    /// the same answer a scope gives. A caller that may not read the email is told it was
    /// not found rather than refused, for the reason an account this deployment no longer serves is: a refusal would
    /// confirm the identifier exists.
    /// </remarks>
    public async Task<bool> IsReadableAsync(
        MailboxReaderKind reader,
        MailAccountId accountId,
        MailFolderAlias? folderAlias,
        CancellationToken cancellationToken) =>
        this.IsReadable(reader, await this.ReadAssignedAccountsAsync(cancellationToken), accountId, folderAlias);

    /// <summary>Reports whether a reader may reach one email, given the accounts the user in hand was already read to be assigned.</summary>
    /// <param name="reader">Who the read or the act is answering for.</param>
    /// <param name="assignedAccounts">The accounts <see cref="ReadAssignedAccountsAsync" /> answered, read once for every email asked about.</param>
    /// <param name="accountId">The account the email was read from.</param>
    /// <param name="folderAlias">The alias of the folder the email is in, or <see langword="null" /> for a folder no declaration names.</param>
    /// <returns>The same answer <see cref="IsReadableAsync(MailboxReaderKind, MailAccountId, MailFolderAlias?, CancellationToken)" /> gives, without reading the assignments again.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="reader" /> is not a defined member.</exception>
    public bool IsReadable(
        MailboxReaderKind reader,
        IReadOnlyList<MailAccountId> assignedAccounts,
        MailAccountId accountId,
        MailFolderAlias? folderAlias)
    {
        if (!Enum.IsDefined(reader))
        {
            throw new ArgumentOutOfRangeException(nameof(reader), reader, "A read answers either for an agent or for the person.");
        }

        return assignedAccounts.Contains(accountId)
            && (folderAlias is not { } declared
                || this.folderParticipation.GetParticipation(accountId, declared).Admits(reader));
    }

    /// <summary>Reports whether a reader may read one stored email, its spam verdict included.</summary>
    /// <param name="reader">Who the read is answering for.</param>
    /// <param name="email">The email, as its summary was read.</param>
    /// <param name="cancellationToken">Cancels the read of the user's assignments.</param>
    /// <returns>The answer <see cref="IsReadable(MailboxReaderKind, IReadOnlyList{MailAccountId}, EmailSummary)" /> gives.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work in hand is acting for no user.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="reader" /> is not a defined member.</exception>
    public async Task<bool> IsReadableAsync(
        MailboxReaderKind reader,
        EmailSummary email,
        CancellationToken cancellationToken) =>
        this.IsReadable(reader, await this.ReadAssignedAccountsAsync(cancellationToken), email);

    /// <summary>Reports whether a reader may read one stored email, given the accounts the user in hand was already read to be assigned.</summary>
    /// <param name="reader">Who the read is answering for.</param>
    /// <param name="assignedAccounts">The accounts <see cref="ReadAssignedAccountsAsync" /> answered.</param>
    /// <param name="email">The email, as its summary was read.</param>
    /// <returns>
    /// <see langword="true" /> when the account and the folder admit <paramref name="reader" /> and, for an agent, the
    /// email is not withheld as spam.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="reader" /> is not a defined member.</exception>
    /// <remarks>
    /// This is what a read of an email's content by its identifier asks, and it is the rule a scope applies to a query,
    /// asked about one email: an agent never reads a message whose verdict is spam, and never reads the junk folder's
    /// mail unless the person marked the message not spam, and never reads a delivery report or a read receipt. A
    /// message withheld that way is answered exactly as one that does not exist, so asking about an identifier tells an
    /// agent nothing about what is there. The acts that reach an
    /// email by its identifier ask <see cref="IsReadable(MailboxReaderKind, IReadOnlyList{MailAccountId}, MailAccountId, MailFolderAlias?)" />
    /// instead: they return no content, and a rule acting on a verdict is not a read.
    /// </remarks>
    public bool IsReadable(
        MailboxReaderKind reader,
        IReadOnlyList<MailAccountId> assignedAccounts,
        EmailSummary email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return this.IsReadable(reader, assignedAccounts, email.Account, email.Folder.Alias)
            && (reader is MailboxReaderKind.Person || (!this.IsWithheldAsSpam(email) && email.Report is null));
    }

    /// <summary>Reports whether an agent's read withholds one email as spam.</summary>
    /// <remarks>
    /// The in-process statement of the rule the stored-email predicate applies to an agent's scope, so the two have to
    /// change together. The junk folder is read from the mapping, which agrees with the folders a scope reads within the
    /// convergence interval the remarks on <see cref="IsReadable(MailboxReaderKind, IReadOnlyList{MailAccountId}, MailAccountId, MailFolderAlias?)" /> state.
    /// </remarks>
    private bool IsWithheldAsSpam(EmailSummary email) =>
        email.Spam is { Verdict: SpamVerdict.Spam }
        || (this.IsInJunkFolder(email)
            && email.Spam is not { DecidedBy: SpamClassificationStage.Person, Verdict: SpamVerdict.NotSpam });

    private bool IsInJunkFolder(EmailSummary email) =>
        email.Folder.Alias is { } alias
        && this.folderReferences.TryResolve(email.Account, MailFolderReference.ToRole(MailFolderSpecialUse.Junk))?.Alias == alias;

    /// <summary>Names the folders a scope answering for one kind of reader is restricted to.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="reader" /> is not a defined member.</exception>
    private static MailFolderSelection ReadableFoldersFor(MailboxReaderKind reader) => reader switch
    {
        MailboxReaderKind.Agent => MailFolderSelection.VisibleToAI,
        MailboxReaderKind.Person => MailFolderSelection.Synchronized,
        _ => throw new ArgumentOutOfRangeException(nameof(reader), reader, "A read answers either for an agent or for the person."),
    };

    /// <summary>Reads the roles a request named folders by, refusing one no account in scope plays.</summary>
    /// <exception cref="MailFolderRoleUnmappedException">Thrown when a role reaches no folder on any account in scope.</exception>
    /// <exception cref="ArgumentException">Thrown when a reference names a folder by its alias, which no request may.</exception>
    /// <remarks>
    /// A role is read against each account's own folders when the query runs, because one query reads several mailboxes
    /// and the folder playing a role is each mailbox's own. It is refused only when no account in scope declares it at
    /// all: refusing because one of several accounts lacks a junk folder would make a general question unanswerable for
    /// the deployments that have the folder.
    /// </remarks>
    private IReadOnlyList<MailFolderSpecialUse> ResolvedRoles(
        IReadOnlyList<MailAccountId> accountsInScope,
        IReadOnlyList<MailFolderReference> folders)
    {
        if (folders.Any(static folder => folder.Alias is not null))
        {
            throw new ArgumentException(
                "A read names a folder by its identity or its role, never by its alias.",
                nameof(folders));
        }

        MailFolderReference[] roles = [.. folders.Where(static folder => folder.Role is not null)];

        foreach (var role in roles.Where(role =>
            !accountsInScope.Any(accountId => this.folderReferences.TryResolve(accountId, role) is not null)))
        {
            throw new MailFolderRoleUnmappedException(role.Role!.Value);
        }

        return [.. roles.Select(static role => role.Role!.Value)];
    }

    /// <summary>Finds the assigned account text names, refusing the request when it names none.</summary>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when no account the user is assigned carries that identifier or display name.</exception>
    private static MailAccountId ResolvedAccountId(
        MailAccountSelector selector,
        IReadOnlyList<ServedMailAccount> assignedAccounts) =>
        assignedAccounts.FirstOrDefault(account => account.IsNamedBy(selector))?.Id
        ?? throw new MailAccountNotAccessibleException(selector);
}
