// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.AutomaticReplies;
using MailFathom.Application.Notifications;
using MailFathom.Application.Signals;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Settlement;

/// <summary>Settles the arrivals of one account whose placement has been decided, and tells a client and the user once.</summary>
/// <remarks>
/// <para>
/// A message synchronization has just committed is settling: its spam verdict may still file it into junk and a rule may
/// still move it, so it is kept from every read until both have had their say. This pass is where that ends. It reads
/// what each settling message still waits on, settles every one that waits on nothing, and only then announces them —
/// the arrival signal an open client refreshes on, and the arrival notification — so a message is seen once, in the
/// folder it ends up in.
/// </para>
/// <para>
/// It runs after the rule pass, in every custody phase, and reads no phase: where a move is committed differs by phase,
/// and the store reads that from the move's own record rather than from the account.
/// </para>
/// <para>
/// It is also where an automatic reply answers: a message settled here is answered once announced, on the same terms
/// as the announcement — only by the replica that settled it, and not again if answering it failed.
/// </para>
/// <para>
/// Nothing is held forever. A message waits at most the verdict's own wait and then
/// <see cref="MaximumPlacementWait" /> more for its rules and its moves, and each message released before what it
/// waited on finished is counted as released rather than settled.
/// </para>
/// </remarks>
public sealed class MailSettlementPass
{
    /// <summary>How long past the verdict's own wait a message is held for its rules and its moves.</summary>
    /// <remarks>
    /// Long enough for a rule pass and a move carried to the server and back over a few runs at the default interval,
    /// which the run brings forward whenever a verdict or a rule asked for something. What outlasts it is stuck rather
    /// than slow, and showing the message where it is serves the user better than hiding it longer.
    /// </remarks>
    public static readonly TimeSpan MaximumPlacementWait = TimeSpan.FromMinutes(15);

    internal const int BatchSize = 200;

    internal const int MaxBatchesPerPass = 50;

    private readonly IMailSettlementStore store;
    private readonly DerivedWorkGate gate;
    private readonly ClientSignals signals;
    private readonly SynchronizationNotifications notifications;
    private readonly IMailSettlementTelemetry telemetry;
    private readonly AutomaticReplyResponder responder;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass.</summary>
    /// <param name="store">Reads the settling messages and writes down the settled ones.</param>
    /// <param name="gate">Reads the spam terms a verdict's wait is decided under.</param>
    /// <param name="signals">Tells an open client that mail arrived.</param>
    /// <param name="notifications">Tells the account's users about mail that reached them.</param>
    /// <param name="telemetry">Counts how messages settled.</param>
    /// <param name="responder">Answers what settled while the account's user's automatic reply runs.</param>
    /// <param name="timeProvider">Reads the instant the waits are measured against and the settlement is written at.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailSettlementPass(
        IMailSettlementStore store,
        DerivedWorkGate gate,
        ClientSignals signals,
        SynchronizationNotifications notifications,
        IMailSettlementTelemetry telemetry,
        AutomaticReplyResponder responder,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.gate = gate;
        this.signals = signals;
        this.notifications = notifications;
        this.telemetry = telemetry;
        this.responder = responder;
        this.timeProvider = timeProvider;
    }

    /// <summary>Settles what can settle of one account's arrivals and announces what this pass settled.</summary>
    /// <param name="account">The account whose arrivals are settled.</param>
    /// <param name="cancellationToken">Cancels the pass between batches; settled batches stay settled.</param>
    /// <returns>How many messages this pass settled, how many of them were announced, and how many it left settling.</returns>
    /// <remarks>
    /// A message another run settled first is neither counted nor announced here, so a competing replica settling the
    /// same arrival raises one signal and one notification between them. Each batch is announced as soon as it is
    /// settled rather than once the walk ends, because a settled message is never read again: a walk that failed or was
    /// cancelled after an earlier batch would otherwise leave that batch shown and never announced. The announcement is
    /// not durable itself: a failure or a cancellation inside it leaves the batch just settled shown without its
    /// notification, because the settlement has committed and nothing reads a settled message again.
    /// </remarks>
    public async Task<MailSettlementReport> RunAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var terms = await this.gate.ReadTermsAsync(cancellationToken);
        var releasedWhenStoredBefore = terms.ReleasedWhenStoredBefore - MaximumPlacementWait;
        var settledCount = 0;
        var announcedCount = 0;
        var stillSettlingCount = 0;
        StoredEmailId? position = null;

        for (var batchNumber = 1; batchNumber <= MaxBatchesPerPass; batchNumber++)
        {
            var batch = await this.store.ReadSettlingAsync(account, position, BatchSize, cancellationToken);
            var decided = batch
                .Select(email => (Email: email, Settlement: email.DecideUnder(terms, releasedWhenStoredBefore)))
                .ToArray();
            var settling = decided.Where(static pair => pair.Settlement is not MailSettlement.Settling).ToArray();
            var settled = await this.SettleAsync(settling, cancellationToken);

            stillSettlingCount += decided.Length - settling.Length;
            settledCount += settled.Length;
            announcedCount += await this.AnnounceAsync(account, settled, cancellationToken);
            await this.responder.AnswerAsync(
                account,
                [.. settled.Where(static email => email.IsAnswerable()).Select(static email => email.Id)],
                cancellationToken);

            if (batch.Count < BatchSize)
            {
                break;
            }

            position = batch[^1].Id;
        }

        return new MailSettlementReport(settledCount, announcedCount, stillSettlingCount);
    }

    private async Task<int> AnnounceAsync(MailAccountId account, SettlingEmail[] settled, CancellationToken cancellationToken)
    {
        if (settled.Length == 0)
        {
            return 0;
        }

        StoredEmailId[] announced = [.. settled.Where(static email => email.IsAnnounced()).Select(static email => email.Id)];

        this.signals.Publish(ClientSignal.MailArrived(account, folder: null, settled.Length));
        await this.notifications.ReportArrivedMailAsync(account, announced, cancellationToken);

        return announced.Length;
    }

    private async Task<SettlingEmail[]> SettleAsync(
        (SettlingEmail Email, MailSettlement Settlement)[] settling,
        CancellationToken cancellationToken)
    {
        if (settling.Length == 0)
        {
            return [];
        }

        var settledIds = (await this.store.SettleAsync(
                [.. settling.Select(static pair => pair.Email.Id)],
                this.timeProvider.GetUtcNow(),
                cancellationToken))
            .ToHashSet();
        var settledHere = settling.Where(pair => settledIds.Contains(pair.Email.Id)).ToArray();

        foreach (var (_, settlement) in settledHere)
        {
            this.telemetry.RecordSettled(settlement);
        }

        return [.. settledHere.Select(static pair => pair.Email)];
    }
}
