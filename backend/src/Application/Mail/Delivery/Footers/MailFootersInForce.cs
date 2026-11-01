// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Decides which footer a message sent now carries, and puts it into the message.</summary>
/// <remarks>
/// <para>
/// The organization holding the account decides first: while it has a template, that template is every member's
/// footer on the account, filled from the member's own record. Otherwise the user's own footer for the account is the
/// footer, and a user with none sends with none.
/// </para>
/// <para>
/// The footer goes in when a message is sent rather than when it is drafted, so a draft never carries one and a
/// reader of a draft never gets it back as words they wrote. That makes the outbox the one place it goes in: every
/// send reaches it as MIME — a new message, an answer, a promoted draft, a held send, and each occasion of a recurring
/// one — so one footer, the one in force at that moment, goes into each of them and into nothing twice.
/// </para>
/// <para>
/// It asks no grant. It is reached from the outbox, which already admitted whoever asked, and from the reading of a
/// user's own footers, which asks its own.
/// </para>
/// <para>
/// <see cref="HasFooterAsync" /> asks neither a grant nor whether the account is the user's, so each of its callers
/// decides both first: the reply drafting asks only about an account assigned to the user drafting, and the Agent's
/// tools only about the accounts the person's own scope reads, on a run admitted to propose mail.
/// </para>
/// </remarks>
/// <param name="store">Holds the footers and templates.</param>
/// <param name="members">Reads the member a template is filled for.</param>
/// <param name="markup">Derives a footer's text from its markup.</param>
/// <param name="composer">Puts a footer into MIME.</param>
/// <param name="bounds">The largest message this deployment sends, which a footer never carries a message past.</param>
public sealed class MailFootersInForce(
    IMailFooterStore store,
    IMemberDirectory members,
    IMailFooterMarkup markup,
    IMailFooterComposer composer,
    OutgoingEmailBounds bounds)
{
    /// <summary>Reads the footer a message one user sends now from one account carries.</summary>
    /// <param name="user">The user sending.</param>
    /// <param name="account">The account sent from.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The footer, or <see langword="null" /> where the message carries none.</returns>
    public async Task<MailFooter?> FindAsync(UserId user, MailAccountId account, CancellationToken cancellationToken)
    {
        var sources = await store.ReadSourcesAsync(user, account, cancellationToken);

        if (sources is { Organization: { } organization, Template: { } template })
        {
            var html = MailFooterTemplate.Fill(
                template.Html,
                await this.ReadSenderAsync(organization, user, cancellationToken));

            return new MailFooter(MailFooterSource.Organization, html, markup.ReadPlainText(html), template.Images);
        }

        return sources.Own is { } own
            ? new MailFooter(MailFooterSource.Own, own.Html, markup.ReadPlainText(own.Html), own.Images)
            : null;
    }

    /// <summary>Reads whether a message one user sends now from one account carries a footer, without filling or reading what it says.</summary>
    /// <param name="user">The user sending.</param>
    /// <param name="account">The account sent from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> where the message carries the user's own footer or the organization's template.</returns>
    /// <remarks>
    /// This is what an agent drafting for the user is told in place of the footer: whether the message closes with one
    /// is all its wording needs, and the footer itself carries the person's name, title, and number.
    /// </remarks>
    public async Task<bool> HasFooterAsync(UserId user, MailAccountId account, CancellationToken cancellationToken)
    {
        var sources = await store.ReadSourcesAsync(user, account, cancellationToken);

        return sources.IsSetByOrganization || sources.Own is not null;
    }

    /// <summary>Appends the footer in force to a message about to be written down for sending.</summary>
    /// <param name="request">The send that was asked for, naming who asked and from which account.</param>
    /// <param name="rawMime">The message as it was composed.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The message carrying its footer, or the message unchanged where it carries none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A send asked for by a caller acting for no user names no author, and so carries no footer: a footer is a
    /// person's, and there is no person to read one for.
    /// </para>
    /// <para>
    /// A footer that would carry the message past the largest one this deployment sends is left off rather than
    /// refusing the message. The message is what the author wrote, and the footer is not theirs to shorten.
    /// </para>
    /// </remarks>
    public async Task<ReadOnlyMemory<byte>> AppendAsync(
        OutgoingEmailRequest request,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Author is not { } author
            || await this.FindAsync(author, request.Account, cancellationToken) is not { } footer)
        {
            return rawMime;
        }

        var footed = composer.Append(rawMime, footer);

        return footed.Length > bounds.MaxMessageBytes ? rawMime : footed;
    }

    private async Task<MailFooterSender> ReadSenderAsync(
        Guid organization,
        UserId user,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(organization, user, cancellationToken);

        return new MailFooterSender(
            member?.DisplayName.Value,
            member?.JobTitle,
            member?.PhoneNumbers.Select(static phoneNumber => phoneNumber.Number).FirstOrDefault());
    }
}
