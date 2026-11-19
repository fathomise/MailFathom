// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Runtime.ExceptionServices;
using MailFathom.Application.AutomaticReplies;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Closes the waits a person kept on a thread when somebody else's reply to it settles.</summary>
/// <param name="summaries">Reads which thread an arrival belongs to, who sent it, and when it reached the mailbox.</param>
/// <param name="contentStore">Holds the raw MIME whose header block says whether a machine sent the arrival.</param>
/// <param name="envelopes">Reads that header block.</param>
/// <param name="tasks">Finds the open waits a thread holds and closes them.</param>
/// <param name="senderIdentities">Names the address the account sends from, whose messages answer nothing.</param>
/// <param name="signals">Tells a person's open clients that their list changed.</param>
/// <remarks>
/// <para>
/// It is called by the settlement, once an arrival has been placed, and only for what settled on this replica. A wait
/// is closed by the write itself, which succeeds only for a wait still open, so two replicas reaching one reply close it
/// once and tell the person once.
/// </para>
/// <para>
/// A message the user sent, in whichever way it came back — from their own address, or as the filed copy of a send —
/// answers nothing. A wait naming a counterpart is closed only by that person; one naming nobody is closed by anybody
/// else in the thread. Addresses are compared in their normalized form, never as written.
/// </para>
/// <para>
/// Only a reply the mailbox received after the wait began answers it, by the date its last receiving hop recorded: a
/// backfill or a resynchronization settles years of a thread's history again, and a message carrying no such date is
/// not an answer because nothing says it is new. An automatic reply and bulk mail answer nothing either, by the claim
/// the message itself makes in <c>Auto-Submitted</c> or <c>Precedence</c>: an out-of-office notice from the person a
/// wait names is not what the person waits for. A posting a mailing list distributed still answers, because a person
/// wrote it and a thread on a list is answered through the list. The header block is read only for an arrival that
/// would close a wait. An arrival with no intact content leaves the reply an answer, since nothing then says a machine
/// sent it; one whose content store failed closes nothing, because a reply nobody could check is not taken as a person's.
/// That failure is held until the batch's other arrivals have been answered and then rethrown, so one unreadable
/// arrival leaves only its own waits open while the settlement still counts the follow-up as failed. Settlement does
/// not retry, so those waits stay open for the person to complete.
/// </para>
/// <para>
/// An account that configures no sending address leaves the user's own address unknown. A wait naming a counterpart is
/// still closed by that counterpart, because a message from them is not the user's own; a wait naming nobody stays
/// open there, because without the address nothing tells a reply from the user's own message.
/// </para>
/// <para>
/// The arrivals' summaries are read once for the batch and the open waits once for each distinct thread in it.
/// </para>
/// </remarks>
public sealed class AwaitedReplies(
    IStoredEmailSummaryReader summaries,
    IEmailContentStore contentStore,
    IArrivedMailEnvelopeReader envelopes,
    IPersonalTaskStore tasks,
    IOutgoingSenderIdentityReader senderIdentities,
    ClientSignals signals)
{
    /// <summary>Closes the waits these arrivals answer.</summary>
    /// <param name="account">The account the mail settled on.</param>
    /// <param name="arrivals">The arrivals that settled on this replica and that a person would be told about.</param>
    /// <param name="cancellationToken">Cancels the answering between arrivals.</param>
    /// <returns>
    /// A task that completes once every wait the arrivals answer is closed and its person told, and that faults with the
    /// first content-store failure only after every other arrival has been answered.
    /// </returns>
    public async Task AnswerAsync(
        MailAccountId account,
        IReadOnlyList<StoredEmailId> arrivals,
        CancellationToken cancellationToken)
    {
        if (arrivals.Count == 0)
        {
            return;
        }

        var own = senderIdentities.FindSenderIdentity(account)?.Address;
        var found = await summaries.ReadSummariesAsync(arrivals, cancellationToken);
        Dictionary<EmailThreadId, IReadOnlyList<PersonalTask>> awaiting = [];
        ExceptionDispatchInfo? unreadable = null;

        foreach (var arrival in arrivals)
        {
            if (!found.TryGetValue(arrival, out var summary)
                || summary is not { ThreadId: { } thread, IsFiledSentCopy: false, ReceivedAt: { } receivedAt }
                || !EmailAddress.TryCreate(displayName: null, summary.SenderAddress, out var sender)
                || own == sender)
            {
                continue;
            }

            if (!awaiting.TryGetValue(thread, out var waits))
            {
                awaiting[thread] = waits = await tasks.ReadAwaitingAnswerAsync(thread, cancellationToken);
            }

            var answer = new TaskAnswer(arrival, receivedAt);
            PersonalTask[] answered =
            [
                .. waits.Where(task => task.IsAnswerableBy(answer)
                    && (task.Counterpart is { } counterpart ? counterpart.Address == sender : own is not null)),
            ];

            if (answered.Length == 0)
            {
                continue;
            }

            try
            {
                if (await this.CameFromAMachineAsync(arrival, cancellationToken))
                {
                    continue;
                }
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                unreadable ??= ExceptionDispatchInfo.Capture(failure);

                continue;
            }

            foreach (var task in answered)
            {
                if (await tasks.AnswerAsync(task.User, task.Id, answer, cancellationToken) is PersonalTaskChangeOutcome.Applied)
                {
                    signals.Publish(ClientSignal.TasksChanged(task.User));
                }
            }
        }

        unreadable?.Throw();
    }

    private async Task<bool> CameFromAMachineAsync(StoredEmailId arrival, CancellationToken cancellationToken)
    {
        var content = await contentStore.FindStoredContentAsync(arrival, cancellationToken);

        return content is not null
            && content.FindIntegrityDefect() is null
            && envelopes.Read(content.RawMime)?.Automation is EmailAutomation.AutomaticallySubmitted or EmailAutomation.BulkPrecedence;
    }
}
