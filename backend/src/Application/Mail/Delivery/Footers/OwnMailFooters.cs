// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Reads and writes the signed-in user's own footers, and reads the footer each of their accounts sends with.</summary>
/// <remarks>
/// A footer is about the person writing it rather than about the mail, so it is read and written under the profile
/// grants. On a shared mailbox each person assigned it writes their own, and none of them writes anybody else's.
/// </remarks>
/// <param name="authorization">Reports the grant the caller holds and the user it acts for.</param>
/// <param name="store">Holds the footers.</param>
/// <param name="assignments">Answers which accounts are the user's.</param>
/// <param name="markup">Sanitizes what the user writes.</param>
/// <param name="inForce">Decides the footer each account sends with.</param>
/// <param name="composer">Composes a footer as a message, so it is reduced the way a message is.</param>
/// <param name="renderer">Reduces that message to the document a client draws, its images inlined.</param>
public sealed class OwnMailFooters(
    AccessAuthorization authorization,
    IMailFooterStore store,
    IMailAccountAssignments assignments,
    IMailFooterMarkup markup,
    MailFootersInForce inForce,
    IMailFooterComposer composer,
    IEmailContentRenderer renderer)
{
    /// <summary>Reads the signed-in user's own footer for one of their accounts.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The footer and whether the organization sets it instead, or <see langword="null" /> where the account is not one of the user's.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileRead" />.</exception>
    public async Task<OwnMailFooter?> ReadAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.ProfileRead);

        var user = authorization.RequireUser();

        if (!await this.IsAssignedAsync(user, account, cancellationToken))
        {
            return null;
        }

        var sources = await store.ReadSourcesAsync(user, account, cancellationToken);

        return new OwnMailFooter(sources.Own, sources.IsSetByOrganization);
    }

    /// <summary>Replaces the signed-in user's own footer for one of their accounts, or says why it may not be saved.</summary>
    /// <param name="account">The account.</param>
    /// <param name="html">The footer's markup as the user wrote it.</param>
    /// <param name="images">The images uploaded with it.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    /// <returns>What became of the write.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="html" /> or <paramref name="images" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileWrite" />.</exception>
    /// <remarks>
    /// An account whose organization sets a template is refused rather than written: the template is what goes out from
    /// it, and a footer of the user's own saved beside it would be one they believe is sent and is not.
    /// </remarks>
    public async Task<MailFooterWrite> SaveAsync(
        MailAccountId account,
        string html,
        IReadOnlyList<MailFooterImage> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(images);

        authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        var user = authorization.RequireUser();

        if (!await this.IsAssignedAsync(user, account, cancellationToken))
        {
            return new MailFooterWrite(MailFooterWriteOutcome.AccountNotAssigned);
        }

        if ((await store.ReadSourcesAsync(user, account, cancellationToken)).IsSetByOrganization)
        {
            return new MailFooterWrite(MailFooterWriteOutcome.SetByOrganization);
        }

        if (MailFooterContent.Read(html, images, markup, out var refusal) is not { } footer)
        {
            return new MailFooterWrite(MailFooterWriteOutcome.Refused, Refusal: refusal);
        }

        return await store.SaveOwnAsync(user, account, footer, cancellationToken)
            ? new MailFooterWrite(MailFooterWriteOutcome.Saved, footer)
            : new MailFooterWrite(MailFooterWriteOutcome.AccountNotAssigned);
    }

    /// <summary>Removes the signed-in user's own footer for one of their accounts.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><see langword="true" /> when the account is one of the user's, whether or not they had a footer on it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileWrite" />.</exception>
    public async Task<bool> RemoveAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        var user = authorization.RequireUser();

        if (!await this.IsAssignedAsync(user, account, cancellationToken))
        {
            return false;
        }

        await store.RemoveOwnAsync(user, account, cancellationToken);

        return true;
    }

    /// <summary>Reads, for each of the signed-in user's accounts, the footer a message sent from it now would carry.</summary>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>One reading per account that sends with a footer, in the order the accounts are assigned; none for an account sending without one.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileRead" />.</exception>
    /// <remarks>
    /// The footer is reduced as a message carrying it would be, so its images arrive inlined in the document exactly as
    /// a message's own inline images do, and a client fetches nothing to draw it.
    /// </remarks>
    public async Task<IReadOnlyList<MailFooterInForceReading>> ReadInForceAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.ProfileRead);

        var user = authorization.RequireUser();
        var readings = new List<MailFooterInForceReading>();

        foreach (var account in await assignments.ReadAccountsAssignedToAsync(user, cancellationToken))
        {
            if (await inForce.FindAsync(user, account, cancellationToken) is { } footer)
            {
                readings.Add(new MailFooterInForceReading(
                    account,
                    footer.Source,
                    await this.ReduceAsync(footer, cancellationToken)));
            }
        }

        return readings;
    }

    private async Task<bool> IsAssignedAsync(UserId user, MailAccountId account, CancellationToken cancellationToken) =>
        (await assignments.ReadAccountsAssignedToAsync(user, cancellationToken)).Contains(account);

    private async Task<MailDocument?> ReduceAsync(MailFooter footer, CancellationToken cancellationToken)
    {
        var message = composer.ComposeAlone(footer);

        var rendered = await renderer.RenderAsync(
            new StoredEmailContent(message, message.Length, ReadOnlyMemory<byte>.Empty),
            new EmailContentRenderingBounds(
                IncludeSanitizedHtml: false,
                MailFooterContent.MaximumHtmlLength,
                MailFooterContent.MaximumHtmlLength)
            {
                IncludeMailDocument = true,
                RemainingInlineImageOctetsForRead = MailDocumentBounds.Default.MaximumInlineImageOctetsPerDocument,
            },
            cancellationToken);

        return rendered.Rendering?.Document;
    }
}
