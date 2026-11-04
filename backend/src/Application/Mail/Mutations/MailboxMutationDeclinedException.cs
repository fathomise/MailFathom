// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Indicates that the mail server answered a command carrying a change and did not take it.</summary>
/// <remarks>
/// <para>
/// It is the refusal the server states in its answer to the command itself — a <c>NO</c> or a <c>BAD</c>, a folder it
/// reports gone, a folder it opened read-only, an occurrence whose UIDVALIDITY no longer holds — as opposed to the two
/// refusals MailFathom reaches before any command goes out, which have types of their own. Which class it is was read
/// by the adapter that saw the answer, because only it knows what the answer was made of.
/// </para>
/// <para>
/// The message names the account and folder aliases and nothing the server said: a response text is the server's own
/// prose, which can carry a remote path or an address, and an operator reading the record needs the code, not the text.
/// </para>
/// </remarks>
public sealed class MailboxMutationDeclinedException : MailboxMutationRefusedException
{
    /// <summary>Initializes a new refusal naming where the change was declined and whether asking again could help.</summary>
    /// <param name="accountId">The account the change was asked of.</param>
    /// <param name="folderAlias">The folder the email is in.</param>
    /// <param name="refusalClass">Whether asking again could alter the server's answer.</param>
    /// <param name="innerException">The mail-library failure that carried the server's answer.</param>
    public MailboxMutationDeclinedException(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        MailboxMutationRefusalClass refusalClass,
        Exception innerException)
        : base(
            $"The mail server for {accountId.Value}/{folderAlias.Value} answered a change and did not take it.",
            innerException)
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
        this.RefusalClass = refusalClass;
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxMutationDeclined;

    /// <inheritdoc />
    public override MailboxMutationRefusalClass RefusalClass { get; }

    /// <summary>Gets the account the change was asked of.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the folder the email is in.</summary>
    public MailFolderAlias FolderAlias { get; }
}
