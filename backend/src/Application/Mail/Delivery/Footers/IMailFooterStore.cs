// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Holds each user's own footer per mail account and each organization's footer template.</summary>
/// <remarks>
/// A user's footer hangs off their assignment to the account and a template off its organization, so ending either
/// takes the footer with it: a footer carries a person's name and number, and it has no purpose once nobody can send
/// with it.
/// </remarks>
public interface IMailFooterStore
{
    /// <summary>Reads one user's own footer for one account.</summary>
    /// <param name="user">The user.</param>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The footer, or <see langword="null" /> where the user wrote none for the account.</returns>
    Task<MailFooterContent?> ReadOwnAsync(UserId user, MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Replaces one user's own footer for one account.</summary>
    /// <param name="user">The user.</param>
    /// <param name="account">The account.</param>
    /// <param name="footer">The whole footer, its images included.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when it was saved, and <see langword="false" /> where the user is not assigned the account.</returns>
    Task<bool> SaveOwnAsync(
        UserId user,
        MailAccountId account,
        MailFooterContent footer,
        CancellationToken cancellationToken);

    /// <summary>Removes one user's own footer for one account, which reports nothing when there was none.</summary>
    /// <param name="user">The user.</param>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the footer is gone.</returns>
    Task RemoveOwnAsync(UserId user, MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Reads one organization's footer template.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The template, or <see langword="null" /> where none is set.</returns>
    Task<MailFooterContent?> ReadTemplateAsync(Guid organization, CancellationToken cancellationToken);

    /// <summary>Replaces one organization's footer template.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="footerTemplate">The whole template, its images included.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when it was saved, and <see langword="false" /> where this deployment holds no such organization.</returns>
    Task<bool> SaveTemplateAsync(Guid organization, MailFooterContent footerTemplate, CancellationToken cancellationToken);

    /// <summary>Removes one organization's footer template, which reports nothing when there was none.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the template is gone.</returns>
    Task RemoveTemplateAsync(Guid organization, CancellationToken cancellationToken);

    /// <summary>Reads everything that decides the footer one user's message from one account carries.</summary>
    /// <param name="user">The user sending.</param>
    /// <param name="account">The account sent from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organization holding the account with its template, and the user's own footer for it.</returns>
    Task<MailFooterSources> ReadSourcesAsync(UserId user, MailAccountId account, CancellationToken cancellationToken);
}
