// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Reads the accounts a claim about mapped folders is judged against, out of the bound declarations that hold them.</summary>
/// <remarks>
/// <para>
/// Identifiers are trimmed and blanks are dropped, which is what <see cref="Domain.Accounts.MailAccountId" />
/// does to the same text. A blank identifier is the record's own defect to report, and reporting it again here would
/// name the wrong document.
/// </para>
/// <para>
/// A folder alias that is not a value this system issues is dropped for the same reason. Every mapped folder is read,
/// including one the account does not mirror, because a mapping is the whole of what a destination needs: such a folder
/// is resolved when a change first files into it rather than by a run of its own. Each folder is read with the role it
/// plays beside its alias, because a destination may be named either way.
/// </para>
/// </remarks>
internal static class DeclaredMailAccounts
{
    /// <summary>Reads the declared accounts from one bound set of mailbox declarations.</summary>
    /// <param name="accounts">The declarations, which may be one user's own rather than the whole deployment's.</param>
    /// <returns>The accounts, in the order they are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Every claim a user's record makes about a folder — their scanned folders and their junk destination — resolves
    /// within their own accounts and nowhere else, so it is judged through this reading of them.
    /// </remarks>
    public static IReadOnlyCollection<DeclaredMailAccount> ReadFrom(
        IEnumerable<MailSynchronizationAccountOptions> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return
        [
            .. accounts
                .Where(account => !string.IsNullOrWhiteSpace(account.AccountId))
                .Select(account => new DeclaredMailAccount(account.AccountId.Trim(), ReadMappedFolders(account))),
        ];
    }

    /// <summary>Reads one account's mapped folders from the bound folders it is actually run with.</summary>
    private static IReadOnlyCollection<DeclaredMailFolder> ReadMappedFolders(MailSynchronizationAccountOptions account) =>
    [
        .. account.EffectiveFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Alias) && !folder.Alias.Any(char.IsControl))
            .Select(folder => new DeclaredMailFolder(
                MailFolderAlias.Create(folder.Alias),
                folder.DeclaredRole,
                folder.Participation.IsVisibleToAI)),
    ];
}
