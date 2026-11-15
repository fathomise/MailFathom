// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Mutations.Authoring.Idempotency;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Deletes a draft through the stored message it is filed as, while that message holds no occurrence a mutation record could name.</summary>
/// <remarks>
/// <para>
/// A revision is stored as a message the drafts folder lists before any occurrence is bound to it: on an account whose
/// source is the truth the copy is appended afterwards and synchronization binds it later still, and on an account
/// MailFathom holds alone no source ever gives it one. Until then a move or a delete has nothing to issue against and
/// nothing to commit a change of, and answering that the message is not there would put the row straight back in front
/// of the person who deleted it.
/// </para>
/// <para>
/// What a delete of such a message can truthfully do is give the draft up, which is the act the draft's own route
/// performs: the record is marked given up in one commit with the key of the act, and the account's custodian settles
/// it — withdrawing the copies it appended where a source holds any, erasing the stored message, and removing the
/// record. So the message is erased rather than kept in the trash, and a settlement that could not finish is one the
/// draft pass finishes, exactly as it finishes a draft given up by its identifier.
/// </para>
/// <para>
/// Only the person who wrote the draft is answered here, for the reason
/// <see cref="MailDraftBook.DiscardAsync" /> gives: a draft is its author's among the account's assigned users. Anybody
/// else deleting the message is told what they were told before, until an occurrence is bound and the message is mail
/// in the folder like any other.
/// </para>
/// </remarks>
internal sealed class UnboundDraftDeletion(
    MailboxScopeResolver scopeResolver,
    IMailDraftStore drafts,
    MailDraftFiler draftFiler,
    OptimisticConcurrencyRetryPolicy commitPolicy,
    MailActIdempotency idempotency,
    TimeProvider timeProvider)
{
    /// <summary>Finds the draft the person is still writing that a stored message without an occurrence shows.</summary>
    /// <param name="storedEmailId">The message no occurrence was found for.</param>
    /// <param name="reader">Who the act is asked for, which has to be the person.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The draft, or <see langword="null" /> where the message shows none of the person's own in an account they may still read.</returns>
    internal async Task<MailDraftRecord?> FindAsync(
        StoredEmailId storedEmailId,
        MailboxReaderKind reader,
        CancellationToken cancellationToken)
    {
        if (reader is not MailboxReaderKind.Person
            || await drafts.FindFiledAsAsync(scopeResolver.User, storedEmailId, cancellationToken) is not { } draft)
        {
            return null;
        }

        return await scopeResolver.IsReadableAsync(reader, draft.AccountId, draft.FiledFolder, cancellationToken)
            ? draft
            : null;
    }

    /// <summary>Gives the draft up and has the account's custodian settle it.</summary>
    /// <param name="draft">The draft <see cref="FindAsync" /> answered.</param>
    /// <param name="storedEmailId">The message the person deleted, which the key is kept against.</param>
    /// <param name="key">The key the person's client generated for the act, or <see langword="null" /> for a caller that makes none.</param>
    /// <param name="outcome">What the act answers, which a repeat under the key is answered again.</param>
    /// <param name="cancellationToken">Cancels the write and the settlement.</param>
    /// <returns>What the first attempt answered where the key was already used on this message, and <see langword="null" /> where this call gave the draft up.</returns>
    /// <remarks>
    /// The settlement is not part of the answer. The draft is given up once the commit is durable, so a copy that could
    /// not be withdrawn or a server that could not be reached leaves work the draft pass owns rather than a delete that
    /// did not happen.
    /// </remarks>
    internal async Task<MailActOutcome?> GiveUpAsync(
        MailDraftRecord draft,
        StoredEmailId storedEmailId,
        MailActIdempotencyKey? key,
        MailActOutcome outcome,
        CancellationToken cancellationToken)
    {
        MailActOutcome? replayed = null;

        await commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                replayed = null;

                if (key is not null
                    && await idempotency.FindAsync(session, key, storedEmailId, attemptCancellationToken) is { } first)
                {
                    replayed = first;

                    return;
                }

                await drafts.RecordDiscardedAsync(session, draft.Id, timeProvider.GetUtcNow(), attemptCancellationToken);

                if (key is not null)
                {
                    await idempotency.StageAsync(session, key, storedEmailId, outcome, attemptCancellationToken);
                }
            },
            cancellationToken);

        if (replayed is not null)
        {
            return replayed;
        }

        // Read again, because settling acts on the record that says it was given up. One already gone was settled by
        // whoever else gave it up meanwhile.
        if (await drafts.FindAsync(draft.Id, cancellationToken) is { } givenUp)
        {
            await draftFiler.SettleAsync(givenUp, cancellationToken);
        }

        return null;
    }
}
