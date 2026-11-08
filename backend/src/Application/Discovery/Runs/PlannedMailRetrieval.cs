// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.Application.Discovery.Runs;

/// <summary>Runs a retrieval plan's lookups against the mail the question's scope admits, and stops when it has enough.</summary>
/// <remarks>
/// <para>
/// It implements no retrieval of its own. Every lookup goes through <see cref="IEmailKnowledgeSearch" />, which is the
/// deployment's own lexical, semantic, or hybrid path, so whatever those paths have been taught to reach — an
/// attachment's own text, a description of a picture — is what a plan reaches, ranked as they rank it. Nothing here
/// re-derives or re-ranks around what they returned.
/// </para>
/// <para>
/// The scope is the question's and it bounds the reading rather than the result: it travels into each lookup and is
/// applied by the query itself, so a question about four selected messages reads four messages instead of reading the
/// mailbox and discarding the rest.
/// </para>
/// <para>
/// What a plan may draw out in total is the run's, not a lookup's. Each lookup's passages pass the run's own ledger,
/// which admits what still fits inside the characters one question may retrieve — the privacy ceiling, being the total
/// amount of somebody's mail that may leave the process to answer one question — and says so when a lookup found more
/// than that. Reaching it stops the plan rather than cutting the answer, and the run states it as a limitation instead
/// of quietly answering from less.
/// </para>
/// </remarks>
public sealed class PlannedMailRetrieval
{
    private readonly IEmailKnowledgeSearch knowledgeSearch;
    private readonly MailAnsweringRunLedger ledger;

    /// <summary>Creates the retrieval a plan is run through.</summary>
    /// <param name="knowledgeSearch">The deployment's own ranked retrieval over the caller's mail.</param>
    /// <param name="ledger">Counts what this run has drawn out of the mailbox, and admits only what still fits inside its ceiling.</param>
    public PlannedMailRetrieval(IEmailKnowledgeSearch knowledgeSearch, MailAnsweringRunLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(knowledgeSearch);
        ArgumentNullException.ThrowIfNull(ledger);

        this.knowledgeSearch = knowledgeSearch;
        this.ledger = ledger;
    }

    /// <summary>Runs the plan's lookups in order, giving each its share of the passages the plan allows.</summary>
    /// <param name="question">The question, whose scope bounds every lookup.</param>
    /// <param name="plan">The plan to run.</param>
    /// <param name="progress">Told how far the plan has got as each lookup settles, or <see langword="null" /> where nobody is watching.</param>
    /// <param name="cancellationToken">Cancels the retrieval between and during lookups.</param>
    /// <returns>What the run may answer from, with what it took to find it.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Every lookup the plan holds carried a filter this deployment refuses.</exception>
    /// <remarks>
    /// <para>
    /// Every lookup is admitted up to an equal share of <see cref="RetrievalPlan.MessageAllowance" /> first, and only
    /// what that leaves unspent goes to the passages a lookup found beyond its share, taken rank by rank across the
    /// lookups. So a later lookup's best passages are never crowded out by an earlier lookup's lower-ranked ones: a plan
    /// lists several short wordings because it cannot know which one reaches the evidence, and the one that does is as
    /// often the last as the first. A cut of a message already handed over spends no share, since the shares count
    /// messages: two lookups cut one message around different words, and the second cut is handed over beside the first
    /// rather than crowding out a message nothing else reached. The allowance assures every lookup a share, so every
    /// lookup runs; only a deployment whose retrieval returns fewer passages than the plan holds lookups gives each one
    /// passage and leaves the lookups past that many unrun, since nothing they found could be admitted.
    /// </para>
    /// <para>
    /// One refused lookup is skipped rather than fatal: the filters are derived from a question by a model, and a
    /// question is not unanswerable because one of several wordings named an address that is not one. A plan whose every
    /// lookup was refused is a different thing — there is nothing left to answer from, and the caller is told why rather
    /// than handed an empty result that reads as a mailbox holding nothing.
    /// </para>
    /// <para>
    /// The progress callback is a plain delegate rather than an <see cref="IProgress{T}" />, and each report is awaited
    /// before the next lookup is issued. That is the whole reason: the standard implementation posts each report to a
    /// synchronization context or to the thread pool, so two reports can be observed in the order they were scheduled
    /// rather than the order they were made — and a caller turning them into a numbered sequence would write a plan
    /// that went backwards. Awaiting is what extends that property to a caller whose report is a write: two writes
    /// never overlap, so the run stays the one writer of its own record. A caller wanting them elsewhere hands over a
    /// delegate that puts them there.
    /// </para>
    /// </remarks>
    public Task<DiscoveryEvidence> RetrieveAsync(
        MailQuestion question,
        RetrievalPlan plan,
        Func<DiscoveryRetrievalProgress, Task>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(plan);

        return this.RunLookupsAsync(question, plan, plan.Lookups, earlier: null, progress, cancellationToken);
    }

    /// <summary>Runs the lookups the follow-up round added, each assured a few messages the plan did not hand over.</summary>
    /// <param name="question">The question, whose scope bounds every lookup.</param>
    /// <param name="plan">The plan whose lookups have already run.</param>
    /// <param name="followUpLookups">The lookups the round added, at most <see cref="RetrievalPlan.MaximumFollowUpLookups" />.</param>
    /// <param name="earlier">What the plan's own lookups found.</param>
    /// <param name="progress">Told how far retrieval has got as each lookup settles, counting the plan's lookups and these together.</param>
    /// <param name="cancellationToken">Cancels the retrieval between and during lookups.</param>
    /// <returns>What the plan found with what these lookups added after it, and the lookups themselves.</returns>
    /// <remarks>
    /// <para>
    /// The round does not share the plan's <see cref="RetrievalPlan.MessageAllowance" />, which the plan's own lookups
    /// usually fill: a follow-up lookup exists to reach the message holding the answer that no plan lookup reached, so
    /// one admitted only into what the plan left would hand nothing over in exactly the run it was asked for. Each lookup
    /// here is assured <see cref="RetrievalPlan.MessagesAssuredPerLookup" /> messages of its own instead, within the
    /// sources one result may declare, <see cref="PresentationEvidence.MaxCitations" />, so a message handed over is
    /// never one the composition cannot be shown. A cut of a message already handed over is admitted as it is for the
    /// plan's own lookups, which is often what the round is for — the same message, cut around the word the answer stands
    /// beside — and every passage passes the same ledger, whose ceiling on retrieved characters is the run's and is what
    /// stops a lookup here.
    /// </para>
    /// <para>
    /// A refused lookup is skipped, and a round whose every lookup was refused ends with what the plan found rather than
    /// with the refusal, since the run still has something to answer from.
    /// </para>
    /// </remarks>
    public Task<DiscoveryEvidence> RetrieveFollowUpAsync(
        MailQuestion question,
        RetrievalPlan plan,
        IReadOnlyList<EmailKnowledgeQuery> followUpLookups,
        DiscoveryEvidence earlier,
        Func<DiscoveryRetrievalProgress, Task>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(followUpLookups);
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(followUpLookups.Count, RetrievalPlan.MaximumFollowUpLookups);

        return followUpLookups.Count is 0 || earlier.RetrievalTruncated
            ? Task.FromResult(earlier)
            : this.RunLookupsAsync(question, plan, followUpLookups, earlier, progress, cancellationToken);
    }

    private async Task<DiscoveryEvidence> RunLookupsAsync(
        MailQuestion question,
        RetrievalPlan plan,
        IReadOnlyList<EmailKnowledgeQuery> lookups,
        DiscoveryEvidence? earlier,
        Func<DiscoveryRetrievalProgress, Task>? progress,
        CancellationToken cancellationToken)
    {
        var found = new List<EmailKnowledgePassage>(earlier?.Passages ?? []);
        var alreadyFound = found.Select(IdentityOf).ToHashSet();
        var messagesFound = found.Select(static passage => passage.StoredEmailId.Value).ToHashSet();
        var beyondShare = new List<IReadOnlyList<EmailKnowledgePassage>>(lookups.Count);
        var messageAllowance = earlier is null
            ? plan.MessageAllowance
            : Math.Max(
                messagesFound.Count,
                Math.Min(messagesFound.Count + (lookups.Count * RetrievalPlan.MessagesAssuredPerLookup), PresentationEvidence.MaxCitations));
        var share = Math.Max(1, (messageAllowance - messagesFound.Count) / lookups.Count);
        var retrievalMode = earlier?.RetrievalMode ?? EmailSearchRetrievalMode.Lexical;
        var lookupsPlanned = earlier is null ? lookups.Count : plan.Lookups.Count + lookups.Count;
        var lookupsRun = 0;
        var lookupsRefused = 0;
        MailboxQueryFilterInvalidException? lastRefusal = null;

        foreach (var lookup in lookups)
        {
            EmailKnowledgeLookup? retrieved = null;
            try
            {
                retrieved = await this.knowledgeSearch.FindPassagesAsync(question.Scope, lookup, cancellationToken);
            }
            catch (MailboxQueryFilterInvalidException refusal)
            {
                lastRefusal = refusal;
                lookupsRefused++;
            }

            if (retrieved is not null)
            {
                // Taken from the first lookup that ran rather than from the last, because the mode describes how this
                // deployment ranks and every lookup of one run is therefore ranked the same way.
                if (lookupsRun is 0 && earlier is null)
                {
                    retrievalMode = retrieved.RetrievalMode;
                }

                lookupsRun++;

                // Cut to the lookup's share before the ledger is asked, so a lookup returning twenty passages of new
                // messages charges the run for its share alone: what it found beyond that is held back uncharged, and
                // reaches the ledger only if the rest of the plan leaves room for it. A further cut of a message already
                // found is not capped by the share and is charged at once, since the ledger rather than the share is
                // what bounds it.
                var (taken, held) = Split(
                    NotYetFound(retrieved.Passages, alreadyFound),
                    messagesFound,
                    Math.Min(share, messageAllowance - messagesFound.Count));

                beyondShare.Add(held);
                found.AddRange(Admit(taken));
            }

            if (lookupsRun + lookupsRefused == lookups.Count && !this.ledger.RetrievalWasTruncated)
            {
                found.AddRange(Admit(Split(
                    RankByRank(beyondShare, alreadyFound),
                    messagesFound,
                    messageAllowance - messagesFound.Count).Taken));
            }

            await ReportAsync();

            // A ceiling that is reached stops the lookups rather than cutting one: nothing a later lookup found would fit
            // either, so issuing it would read more of somebody's mail out of the database to discard it. A full
            // allowance stops the plan's own lookups for the same reason, and not the follow-up's, whose cuts of messages
            // already handed over still fit.
            if (this.ledger.RetrievalWasTruncated || (earlier is null && messagesFound.Count >= messageAllowance))
            {
                break;
            }
        }

        if (earlier is null && lookupsRun is 0 && lastRefusal is not null)
        {
            throw lastRefusal;
        }

        return new DiscoveryEvidence(
            found,
            retrievalMode,
            (earlier?.LookupsRun ?? 0) + lookupsRun,
            (earlier?.LookupsRefused ?? 0) + lookupsRefused,
            this.ledger.RetrievalWasTruncated)
        {
            FollowUpLookups = earlier is null ? [] : [.. lookups],
        };

        Task ReportAsync() => progress?.Invoke(new DiscoveryRetrievalProgress(
            (earlier?.LookupsRun ?? 0) + lookupsRun,
            (earlier?.LookupsRefused ?? 0) + lookupsRefused,
            lookupsPlanned,
            found.Count)) ?? Task.CompletedTask;

        IReadOnlyList<EmailKnowledgePassage> Admit(IReadOnlyList<EmailKnowledgePassage> passages)
        {
            var admitted = this.ledger.AdmitPassages(passages);
            alreadyFound.UnionWith(admitted.Select(IdentityOf));
            messagesFound.UnionWith(admitted.Select(static passage => passage.StoredEmailId.Value));

            return admitted;
        }
    }

    private static List<EmailKnowledgePassage> NotYetFound(
        IReadOnlyList<EmailKnowledgePassage> passages,
        HashSet<(Guid StoredEmailId, string Text)> alreadyFound) =>
        [.. passages.Where(passage => !alreadyFound.Contains(IdentityOf(passage))).DistinctBy(IdentityOf)];

    /// <summary>Orders what the lookups found beyond their shares, every lookup's first before any lookup's second.</summary>
    /// <remarks>The sort is stable, so within one rank the plan's own order decides.</remarks>
    private static List<EmailKnowledgePassage> RankByRank(
        List<IReadOnlyList<EmailKnowledgePassage>> beyondShare,
        HashSet<(Guid StoredEmailId, string Text)> alreadyFound) =>
    [
        .. beyondShare
            .SelectMany(passages => passages.Select((passage, rank) => (Passage: passage, Rank: rank)))
            .OrderBy(ranked => ranked.Rank)
            .Select(ranked => ranked.Passage)
            .Where(passage => !alreadyFound.Contains(IdentityOf(passage)))
            .DistinctBy(IdentityOf),
    ];

    /// <summary>
    /// Takes, in rank order, every further cut of a message already found and the passages of at most
    /// <paramref name="newMessages" /> messages not yet found, and holds back the rest.
    /// </summary>
    /// <remarks>
    /// A further cut costs no share because it adds no message: two lookups cut one message around different words, and
    /// the cut a later lookup made is as often the one carrying the answer as the first. Counting it against the
    /// lookup's share would spend the share on mail already handed over, and crowd out the messages nothing else found.
    /// What bounds it is the ledger, which charges it like any other passage.
    /// </remarks>
    private static (List<EmailKnowledgePassage> Taken, List<EmailKnowledgePassage> Held) Split(
        IEnumerable<EmailKnowledgePassage> candidates,
        HashSet<Guid> messagesFound,
        int newMessages)
    {
        List<EmailKnowledgePassage> taken = [];
        List<EmailKnowledgePassage> held = [];
        var messagesTaken = new HashSet<Guid>();

        foreach (var passage in candidates)
        {
            var message = passage.StoredEmailId.Value;

            if (messagesFound.Contains(message) || messagesTaken.Contains(message))
            {
                taken.Add(passage);
            }
            else if (messagesTaken.Count < newMessages)
            {
                messagesTaken.Add(message);
                taken.Add(passage);
            }
            else
            {
                held.Add(passage);
            }
        }

        return (taken, held);
    }

    /// <summary>What makes two passages one: the same extract of the same message, however many lookups reached it.</summary>
    private static (Guid StoredEmailId, string Text) IdentityOf(EmailKnowledgePassage passage) =>
        (passage.StoredEmailId.Value, passage.Text);
}
