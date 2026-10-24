// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Persists which remote folder each alias is currently bound to.</summary>
/// <remarks>
/// A binding has to be durable before anything is synchronized under it, because the generation it carries is what
/// separates the occurrences and the checkpoint of one binding from those of the next.
/// <para>
/// The port exists for that rule rather than for storage, and no persistence library publishes a contract for it:
/// staging a binding is a comparison against the generation already held, so a competing run that resolved the same
/// alias to a different remote folder is refused instead of merged.
/// </para>
/// </remarks>
public interface IMailFolderResolutionStore
{
    /// <summary>Gets the newest durable binding of an alias.</summary>
    /// <param name="account">The account owning the alias.</param>
    /// <param name="folderAlias">The operator-facing folder name.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The highest-generation binding, or <see langword="null" /> when the alias has never been bound.</returns>
    Task<MailFolderResolution?> GetCurrentResolutionAsync(
        MailAccountId account,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken);

    /// <summary>Gets the alias whose current binding names a remote folder.</summary>
    /// <param name="account">The account owning the bindings.</param>
    /// <param name="remotePath">The remote folder the alias is looked for by.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The alias currently bound to that folder, or <see langword="null" /> when the account binds no alias to it.</returns>
    /// <remarks>
    /// The reverse of the read above, and the only way back from a remote path to the name everything a client is told
    /// is expressed in: a mutation names where it files by the path an IMAP command is issued against, and a folder
    /// crossed into has to be announced by its alias. Only a current binding answers, because a generation the alias has
    /// since moved off names the folder it used to be.
    /// </remarks>
    Task<MailFolderAlias?> GetAliasBoundToAsync(
        MailAccountId account,
        RemoteFolderPath remotePath,
        CancellationToken cancellationToken);

    /// <summary>Stages a binding so the generation exists before any occurrence is stored under it.</summary>
    /// <param name="session">The open session whose transaction the staged insert joins.</param>
    /// <param name="account">The account owning the alias.</param>
    /// <param name="resolution">The binding to stage.</param>
    /// <param name="cancellationToken">Cancels the lookup before anything is staged.</param>
    /// <returns>A task that completes once the binding is staged in the caller's session.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when the generation is already held by a binding naming a different remote folder, which means a
    /// competing run resolved the same alias elsewhere first. Nothing is staged, because adopting that binding would
    /// let one alias generation name two remote folders.
    /// </exception>
    /// <remarks>
    /// Staging a binding that is already durable and identical is a no-op rather than a conflict, so a run that
    /// resolves the same alias to the same remote folder writes nothing.
    /// </remarks>
    Task SaveResolutionAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderResolution resolution,
        CancellationToken cancellationToken);

    /// <summary>Stages a folder row for every declared alias of a mirrored account that names none yet, and retires the row of every alias it declares no longer.</summary>
    /// <param name="session">The open session whose transaction the staged rows join.</param>
    /// <param name="account">The account the aliases are declared on.</param>
    /// <param name="mappings">Every folder the account declares, mirrored or not.</param>
    /// <param name="cancellationToken">Cancels the reads before anything is staged.</param>
    /// <returns>Whether anything was staged or retired.</returns>
    /// <remarks>
    /// A folder declared and not mirrored is never bound by a synchronization run, and is still a folder of the account
    /// that mail is filed into and a reader lists. Its row takes the declared role when no folder plays it yet; a binding
    /// gives it its place and name later.
    /// </remarks>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown on commit when a competing run declared the same folder first.</exception>
    Task<bool> DeclareFoldersAsync(
        IPersistenceSession session,
        MailAccountId account,
        IReadOnlyList<MailFolderMapping> mappings,
        CancellationToken cancellationToken);

    /// <summary>Stages the folder row a mirrored account's alias names, in the shape its current binding gives it.</summary>
    /// <param name="session">The open session whose transaction the staged changes join.</param>
    /// <param name="account">The account owning the alias.</param>
    /// <param name="resolution">The alias's current binding, which is already durable.</param>
    /// <param name="role">The role configuration declares for the alias, or <see langword="null" /> for none.</param>
    /// <param name="cancellationToken">Cancels the reads before anything is staged.</param>
    /// <returns>Whether anything was staged, which is <see langword="false" /> for a folder already in shape and for an account whose custody has moved.</returns>
    /// <remarks>
    /// The row takes the last level of the remote path as its name, sits under the row of the remote parent — one no
    /// alias names is created for an undeclared ancestor — and plays the declared role, and the binding is pointed at
    /// it so the mail stored under the binding is filed there. Only a mirrored account is projected: once custody has
    /// moved, the rows are the hierarchy people edit.
    /// </remarks>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown on commit when a competing run projected the same folder first.</exception>
    Task<bool> ProjectFolderAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderResolution resolution,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken);

    /// <summary>Stages the folder row a mirrored account's alias names at the place a folder act just gave it on the server.</summary>
    /// <param name="session">The open session whose transaction the staged changes join.</param>
    /// <param name="account">The account owning the alias.</param>
    /// <param name="folderAlias">The alias of the folder the act renamed or moved.</param>
    /// <param name="remotePath">Where the server advertises the folder now.</param>
    /// <param name="role">The role configuration declares for the alias, or <see langword="null" /> for none.</param>
    /// <param name="cancellationToken">Cancels the reads before anything is staged.</param>
    /// <returns>Whether anything was staged, which is <see langword="false" /> for an alias no row carries and for an account whose custody has moved.</returns>
    /// <remarks>
    /// The row is shaped exactly as projecting a binding at that path would shape it, and the binding itself is left to
    /// the account's next run, which finds the folder there and projects it the same way. Doing it here is what puts a
    /// renamed or moved folder where the act put it in the tree the act's own re-read draws.
    /// </remarks>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown on commit when a competing run placed the same folder first.</exception>
    Task<bool> PlaceFolderAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderAlias folderAlias,
        RemoteFolderPath remotePath,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken);
}
