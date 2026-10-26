// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.ReplyDrafts;

/// <summary>Reads what one drafting is written from out of PostgreSQL: the conversation, its people, and the account's own manner.</summary>
/// <remarks>
/// <para>
/// Projections rather than entity loads, which is the same privacy control every other mail-derived read applies: each
/// query names the columns a drafting is shown, and no row enters the change tracker on a path that only reads.
/// </para>
/// <para>
/// The context message decides its own conversation and account. Its conversation is what the reply is grounded in,
/// and its account is the only one whose sent mail is read for a manner — asking the caller for either would let a reply
/// be written in one person's voice out of another's correspondence.
/// </para>
/// <para>
/// The style read is inside the caller's scope like every other read here, so a deployment that withholds the sent
/// folder from AI derives no manner from it rather than reaching around the withholding. A conversation whose sent
/// folder is unmapped is the same case and answers the same way: no samples, and a reply written from the
/// correspondence alone.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ReplyDraftSourceStore(
    MailFathomDbContext dbContext,
    IOutgoingSenderIdentityReader senderIdentities)
    : IReplyDraftSourceReader
{
    /// <inheritdoc />
    public async Task<ReplyDraftSources?> ReadSourcesAsync(
        StoredEmailId contextEmailId,
        MailboxScope scope,
        ReplyDraftBounds bounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(bounds);

        var contextMessage = await this.Readable(scope)
            .Where(email => email.Id == contextEmailId.Value)
            .Select(email => new ContextEmailRow(email.MailboxAccountId, email.EmailThreadId))
            .SingleOrDefaultAsync(cancellationToken);

        if (contextMessage is null)
        {
            return null;
        }

        // The chosen message and the recent end of the rest of its conversation, because a reply answers that message
        // and where the exchange got to around it.
        var recent = await ChosenAndMostRecent(
                this.ReadableWithText(scope, contextMessage, contextEmailId),
                contextEmailId,
                bounds.MaximumMessages)
            .Select(email => new ConversationMessageRow(
                email.Id,
                email.Subject,
                email.SenderDisplayName,
                email.SenderAddress,
                email.SentAt,
                email.SearchDocument!.BodyText!.Substring(0, bounds.MaximumCharactersPerMessage)))
            .ToArrayAsync(cancellationToken);

        return Assemble(
            recent,
            contextEmailId,
            await this.AttachmentsAsync(scope, recent, bounds, cancellationToken),
            bounds.MaximumCharactersPerMessage,
            this.SendingAddressOf(contextMessage.MailboxAccountId),
            await this.StyleSamplesAsync(scope, contextMessage, bounds, cancellationToken));
    }

    /// <summary>Orders a conversation's mail with the chosen message first and the rest newest first, and keeps that many of it.</summary>
    /// <param name="emails">The conversation's mail.</param>
    /// <param name="chosenEmailId">The message the person chose, which is kept however old it is.</param>
    /// <param name="count">How many messages to keep, the chosen one among them.</param>
    /// <returns>The chosen message and the most recent of the rest.</returns>
    /// <remarks>
    /// Without the chosen message ahead of the rest, a reply to an early message of a long exchange would be grounded in
    /// the twenty after it and never in the one it answers.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> ChosenAndMostRecent(
        IQueryable<StoredEmailEntity> emails,
        StoredEmailId chosenEmailId,
        int count) =>
        emails
            .OrderByDescending(email => email.Id == chosenEmailId.Value)
            .ThenByDescending(email => email.SentAt)
            .ThenByDescending(email => email.Id)
            .Take(count);

    /// <summary>Orders mail newest first, by when it was sent and then by identity, and keeps the most recent of it.</summary>
    /// <param name="emails">The mail to order.</param>
    /// <param name="count">How many of the most recent messages to keep.</param>
    /// <returns>The most recent messages, newest first.</returns>
    internal static IQueryable<StoredEmailEntity> MostRecent(IQueryable<StoredEmailEntity> emails, int count) =>
        emails
            .OrderByDescending(email => email.SentAt)
            .ThenByDescending(email => email.Id)
            .Take(count);

    /// <summary>Assembles what a drafting is grounded in from the conversation's messages the read kept.</summary>
    /// <param name="recent">The chosen message and the conversation's most recent others, in any order, each already cut to its bound.</param>
    /// <param name="chosenEmailId">The message the person chose, which the turn marks as the one being answered.</param>
    /// <param name="attachments">The attachments those messages carry, in the order each message carries them, each already cut to the per-message bound.</param>
    /// <param name="maximumCharactersPerMessage">What a message and its attachments may carry together.</param>
    /// <param name="sendingNormalizedAddress">The normalized address the mailbox sends from, which names nobody the reply is addressed to.</param>
    /// <param name="styleSamples">The person's own recent sent mail the reply's manner is drawn from.</param>
    /// <returns>The sources, the conversation oldest first and numbered in that order.</returns>
    internal static ReplyDraftSources Assemble(
        IReadOnlyList<ConversationMessageRow> recent,
        StoredEmailId chosenEmailId,
        IReadOnlyList<AttachmentRow> attachments,
        int maximumCharactersPerMessage,
        string? sendingNormalizedAddress,
        IReadOnlyList<string> styleSamples)
    {
        // The order the database cut in, reversed: PostgreSQL sorts a missing date first when descending, so an undated
        // message counts as the newest, and its uuid order is the order of the canonical text form.
        var conversation = recent
            .OrderBy(static row => row.SentAt is null)
            .ThenBy(static row => row.SentAt)
            .ThenBy(static row => row.StoredEmailId.ToString(), StringComparer.Ordinal)
            .ToArray();

        var attachmentsByMessage = attachments.ToLookup(static attachment => attachment.StoredEmailId);

        return new ReplyDraftSources(
            conversation.Length > 0 ? conversation[^1].Subject : null,
            [
                .. conversation.Select((row, position) => new ReplyDraftMessage(
                    StoredEmailId.Create(row.StoredEmailId),
                    position,
                    row.SenderDisplayName,
                    row.SentAt,
                    row.Text,
                    row.StoredEmailId == chosenEmailId.Value,
                    WithinWhatRemains(attachmentsByMessage[row.StoredEmailId], maximumCharactersPerMessage - row.Text.Length))),
            ],
            Participants(conversation, sendingNormalizedAddress),
            styleSamples);
    }

    /// <summary>Cuts a message's attachments to what its own text left of the per-message bound, in the order the message carries them.</summary>
    /// <remarks>
    /// The attachments share the bound a message's text travels under rather than taking one of their own, so reading
    /// them never makes a turn larger than a turn of messages alone could be: what a body leaves unused is what an
    /// attachment may fill, and a message whose own text reaches the bound carries none.
    /// </remarks>
    private static List<ReplyDraftAttachment> WithinWhatRemains(
        IEnumerable<AttachmentRow> attachments,
        int remainingCharacters)
    {
        var kept = new List<ReplyDraftAttachment>();

        foreach (var attachment in attachments)
        {
            if (remainingCharacters <= 0)
            {
                break;
            }

            var text = attachment.Text.Length <= remainingCharacters
                ? attachment.Text
                : MailTextBounds.TruncateAtTextElementBoundary(attachment.Text, remainingCharacters);

            kept.Add(new ReplyDraftAttachment(attachment.FileName, attachment.Kind, text));
            remainingCharacters -= text.Length;
        }

        return kept;
    }

    /// <summary>Reads the people the conversation names, leaving out the account whose reply this is.</summary>
    /// <remarks>
    /// Distinct by the normalized address rather than by the display name, because one correspondent writes under
    /// several spellings of their own name and a list holding each of them would offer the same person three times. The
    /// account's own sending address is left out for the reason nobody drafts a reply to themselves: it is the one
    /// address in the exchange that is never a recipient, and proposing it is a mistake a person then has to undo.
    /// </remarks>
    private static IReadOnlyList<ReplyDraftParticipant> Participants(
        IReadOnlyList<ConversationMessageRow> conversation,
        string? sendingNormalizedAddress) =>
    [
        .. conversation
            .Select(static row => row.SenderAddress)
            .OfType<string>()
            .Where(address => EmailAddress.TryCreate(displayName: null, address, out var parsed)
                && parsed.NormalizedAddress != sendingNormalizedAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(
                (address, position) => new ReplyDraftParticipant(
                    position,
                    NamedIn(conversation, address))),
    ];

    /// <summary>Puts one address back together with the name the conversation most recently wrote it under.</summary>
    private static EmailAddress NamedIn(IReadOnlyList<ConversationMessageRow> conversation, string address)
    {
        var named = conversation
            .LastOrDefault(row => string.Equals(row.SenderAddress, address, StringComparison.OrdinalIgnoreCase));

        EmailAddress.TryCreate(named?.SenderDisplayName, address, out var parsed);

        return parsed;
    }

    /// <summary>Narrows stored mail to what this caller may read.</summary>
    /// <remarks>
    /// It composes the narrowing every mail-returning read composes and states none of its own, which is what stops a
    /// caller's entitlement being read twice and read differently. It is a member of this class rather than an
    /// expression inside an asynchronous method for the reason every other one is: a call made only inside an async
    /// method body belongs to the compiler-generated state machine, and the architecture rule holding every
    /// mail-derived read to this narrowing reads the class.
    /// </remarks>
    private IQueryable<StoredEmailEntity> Readable(MailboxScope scope) =>
        StoredEmailSelectionPredicate.WithinScope(dbContext.StoredEmails.AsNoTracking(), scope);

    /// <summary>Narrows that mail to the context message's conversation, and to the messages a body was extracted from.</summary>
    /// <remarks>
    /// A message whose body extraction has not reached it is in no turn, because a drafting shown a subject and nothing
    /// else would write a reply to a message it never read. A message belonging to no conversation is read on its
    /// own, which is the ordinary shape of a first message somebody replies to.
    /// </remarks>
    private IQueryable<StoredEmailEntity> ReadableWithText(
        MailboxScope scope,
        ContextEmailRow contextMessage,
        StoredEmailId contextEmailId)
    {
        var readable = this.Readable(scope)
            .Where(email => email.SearchDocument != null && email.SearchDocument.BodyText != null);

        return contextMessage.EmailThreadId is { } threadId
            ? readable.Where(email => email.EmailThreadId == threadId)
            : readable.Where(email => email.Id == contextEmailId.Value);
    }

    /// <summary>Reads the words the attachments of the kept messages yielded, each cut to the per-message bound.</summary>
    /// <remarks>
    /// Read off the messages the caller may read rather than off the attachment table directly, so an attachment is
    /// reached through exactly the narrowing its message is. Only an attachment that yielded words is read: one that
    /// was skipped, refused, or never read has nothing to ground a reply in, and a deployment that reads no attachments
    /// holds no such rows at all. Extraction already bounds how many attachments one message records.
    /// </remarks>
    private async Task<IReadOnlyList<AttachmentRow>> AttachmentsAsync(
        MailboxScope scope,
        IReadOnlyList<ConversationMessageRow> messages,
        ReplyDraftBounds bounds,
        CancellationToken cancellationToken)
    {
        var messageIds = messages.Select(static row => row.StoredEmailId).ToArray();

        return await this.Readable(scope)
            .Where(email => messageIds.Contains(email.Id))
            .SelectMany(email => email.AttachmentTexts)
            .Where(attachment => attachment.Text != null)
            .OrderBy(attachment => attachment.StoredEmailId)
            .ThenBy(attachment => attachment.AttachmentPosition)
            .Select(attachment => new AttachmentRow(
                attachment.StoredEmailId,
                attachment.FileName,
                attachment.Kind,
                attachment.Text!.Substring(0, bounds.MaximumCharactersPerMessage)))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Reads the openings of the account's own recent sent mail, or nothing where this deployment derives no manner.</summary>
    /// <remarks>
    /// The context message's conversation is left out of the sample. Its messages are already in the turn, and sending
    /// them twice would spend the request on text the drafting has, while making the person's own last reply the
    /// loudest example of how they write.
    /// </remarks>
    private async Task<IReadOnlyList<string>> StyleSamplesAsync(
        MailboxScope scope,
        ContextEmailRow contextMessage,
        ReplyDraftBounds bounds,
        CancellationToken cancellationToken)
    {
        if (bounds.MaximumStyleMessages <= 0)
        {
            return [];
        }

        var contextThreadId = contextMessage.EmailThreadId;

        // The folder playing the sent role on the account, read off the folder itself, so a held account's sent folder
        // answers exactly as a mirrored one's does.
        return await MostRecent(
                this.Readable(scope).Where(email => email.MailboxAccountId == contextMessage.MailboxAccountId
                    && email.Folder.Role == MailFolderSpecialUse.Sent
                    && email.SearchDocument != null
                    && email.SearchDocument.BodyText != null
                    && (contextThreadId == null || email.EmailThreadId != contextThreadId)),
                bounds.MaximumStyleMessages)
            .Select(email => email.SearchDocument!.BodyText!.Substring(0, bounds.MaximumStyleCharactersPerMessage))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Reads the address the answering account sends from, which is the one participant a reply never proposes.</summary>
    private string? SendingAddressOf(string mailboxAccountId) =>
        senderIdentities.FindSenderIdentity(MailAccountId.Create(mailboxAccountId))?.Address.NormalizedAddress;

    /// <summary>One message of the conversation as the store reads it.</summary>
    /// <param name="StoredEmailId">The message.</param>
    /// <param name="Subject">Its subject.</param>
    /// <param name="SenderDisplayName">The name it was sent under, or <see langword="null" /> where it carried none.</param>
    /// <param name="SenderAddress">The address it was sent from.</param>
    /// <param name="SentAt">When it was sent.</param>
    /// <param name="Text">Its text, cut to what one message may carry.</param>
    internal sealed record ConversationMessageRow(
        Guid StoredEmailId,
        string? Subject,
        string? SenderDisplayName,
        string? SenderAddress,
        DateTimeOffset? SentAt,
        string Text);

    /// <summary>One attachment of a kept message as the store reads it.</summary>
    /// <param name="StoredEmailId">The message carrying it.</param>
    /// <param name="FileName">Its file name, or <see langword="null" /> where it carried none.</param>
    /// <param name="Kind">Whether the words are the file's own or a description of a picture.</param>
    /// <param name="Text">Its words, cut to the per-message bound.</param>
    internal sealed record AttachmentRow(
        Guid StoredEmailId,
        string? FileName,
        AttachmentTextKind Kind,
        string Text);

    private sealed record ContextEmailRow(string MailboxAccountId, Guid? EmailThreadId);
}
