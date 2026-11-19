// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Closes the waits a person kept on a thread when somebody else's reply to it settles.</summary>
/// <param name="summaries">Reads which thread an arrival belongs to and who sent it.</param>
/// <param name="tasks">Finds the open waits a thread holds and closes them.</param>
/// <param name="senderIdentities">Names the address the account sends from, whose messages answer nothing.</param>
/// <param name="signals">Tells a person's open clients that their list changed.</param>
/// <param name="timeProvider">Dates a reply that carried no received date.</param>
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
/// </remarks>
public sealed class AwaitedReplies(
    IStoredEmailSummaryReader summaries,
    IPersonalTaskStore tasks,
    IOutgoingSenderIdentityReader senderIdentities,
    ClientSignals signals,
    TimeProvider timeProvider)
{
    /// <summary>Closes the waits these arrivals answer.</summary>
    /// <param name="account">The account the mail settled on.</param>
    /// <param name="arrivals">The arrivals that settled on this replica and that a person would be told about.</param>
    /// <param name="cancellationToken">Cancels the answering between arrivals.</param>
    /// <returns>A task that completes once every wait the arrivals answer is closed and its person told.</returns>
    public async Task AnswerAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> arrivals,
        CancellationToken cancellationToken)
    {
        if (arrivals.Count == 0 || senderIdentities.FindSenderIdentity(account) is not { } own)
        {
            return;
        }

        HashSet<UserId> changed = [];

        foreach (var arrival in arrivals)
        {
            if (await summaries.FindAsync(arrival, cancellationToken) is not
                { ThreadId: { } thread, IsFiledSentCopy: false } summary
                || !EmailAddress.TryCreate(displayName: null, summary.SenderAddress, out var sender)
                || sender == own.Address)
            {
                continue;
            }

            var answer = new TaskAnswer(arrival, summary.ReceivedAt ?? timeProvider.GetUtcNow());

            foreach (var task in await tasks.ReadAwaitingAnswerAsync(thread, cancellationToken))
            {
                if ((task.Counterpart is null || task.Counterpart.Address == sender)
                    && await tasks.AnswerAsync(task.User, task.Id, answer, cancellationToken) is PersonalTaskChangeOutcome.Applied)
                {
                    changed.Add(task.User);
                }
            }
        }

        foreach (var user in changed)
        {
            signals.Publish(ClientSignal.TasksChanged(user));
        }
    }
}
