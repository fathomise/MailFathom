// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Census;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Counts what one run has spent, refuses what would take it past what one question may cost, and reports what it has spent so far.</summary>
/// <remarks>
/// <para>
/// One instance serves one run, for the reason the run's retrieval does: a ceiling on a question is meaningless if two
/// questions share it. It is nevertheless internally synchronized, because a tool loop may answer several lookups or
/// listings of one run at once, a streamed run reads its counts from a connection other than the one executing it, and
/// both halves of a check-then-take have to happen together.
/// </para>
/// <para>
/// The two kinds of refusal are deliberately unequal. Retrieval — a lookup's passages, a listed page, a census's groups,
/// or a page of a message or of a file it carries — is trimmed to what the run may still send and the run continues,
/// because a question with some mail already retrieved is answerable and the model is told there is no more.
/// A provider call is refused outright, because a run with no allowance for another call has no answer to trim.
/// </para>
/// <para>
/// A token ceiling is checked before a call and can only be checked against what earlier calls reported, so the call
/// that crosses it is paid for. That is inherent rather than an oversight: what a call will cost is not knowable until
/// the provider has answered, and the alternative — estimating it — would refuse calls on a guess. So no refusal here
/// rests on an estimate. The one forecast the ledger makes is <see cref="HasRoomForAtMost" />, which refuses nothing: it
/// tells a run that its next call is likely its last, so the run can make that call the one that answers.
/// </para>
/// <para>
/// It is in this layer rather than beside the agent that first used it because a Discover run is orchestrated here and
/// spends at two ports it reaches the model through, so one ledger for the run has to be a thing this layer can hold.
/// What the run reports about what it spent is <see cref="Read" />, which is
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>'s
/// unit: the run's own consumption against the ceilings that will stop it.
/// </para>
/// </remarks>
public sealed class MailAnsweringRunLedger
{
    private readonly Lock gate = new();
    private readonly HashSet<Guid> retrievedMessages = [];
    private MailAnsweringRunBounds bounds;
    private bool raised;
    private int retrievedCharacters;
    private int providerCalls;
    private long tokens;
    private long costliestCall;

    /// <summary>Initializes a ledger for one run, with nothing spent.</summary>
    /// <param name="bounds">What this run may send, call, and consume.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bounds" /> is <see langword="null" />.</exception>
    public MailAnsweringRunLedger(MailAnsweringRunBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        this.bounds = bounds;
    }

    /// <summary>Gets what this run may send, call, and consume, which is what its counts are read against.</summary>
    public MailAnsweringRunBounds Bounds
    {
        get
        {
            lock (this.gate)
            {
                return this.bounds;
            }
        }
    }

    /// <summary>Reports whether the run's ceilings leave room for at most a given number of further calls.</summary>
    /// <param name="calls">How many further calls to ask about, at least one.</param>
    /// <returns><see langword="true" /> when the run may still call and has room for no more than <paramref name="calls" /> further calls; <see langword="false" /> when it has room for more, or for none.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="calls" /> is below one.</exception>
    /// <remarks>
    /// <para>
    /// By count where that many calls are left, and by tokens where the costliest call so far, made that many times
    /// more, would reach the token ceiling. The second is a forecast rather than a measure — what a call costs is known
    /// only once it is answered — and it is the one estimate this ledger makes, because the alternative is a run that
    /// learns it had no room left only when its next call is refused, with nothing written for the person.
    /// </para>
    /// <para>
    /// Asked with one, it says the next call is the last, and a run told so makes that call without tools, so it answers
    /// from what it has read rather than reading on. A run already past either ceiling has room for none: the next call
    /// is refused.
    /// </para>
    /// </remarks>
    public bool HasRoomForAtMost(int calls)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(calls, 1);

        lock (this.gate)
        {
            var callsLeft = this.bounds.MaximumProviderCalls - this.providerCalls;

            return callsLeft > 0
                && this.tokens < this.bounds.MaximumTokens
                && (callsLeft <= calls || this.tokens + (calls * this.costliestCall) >= this.bounds.MaximumTokens);
        }
    }

    /// <summary>Raises this run's ceilings once, keeping everything it has already spent counted against the new ones.</summary>
    /// <param name="wider">The ceilings the run is held to from here on.</param>
    /// <returns><see langword="true" /> when the ceilings were raised; <see langword="false" /> when this run had already raised them, which leaves them as they are.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="wider" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when any of <paramref name="wider" />'s ceilings is below the one it would replace.</exception>
    /// <remarks>
    /// Once, because a run that could raise its own ceilings repeatedly would have none. Nothing already spent is
    /// forgotten: the move widens what the rest of the run may add. What it does forget is that a lookup was cut,
    /// because the mail that lookup left out is what a run raises its ceilings to read, and a run that reads it under
    /// the new ones was not cut at all.
    /// </remarks>
    public bool Widen(MailAnsweringRunBounds wider)
    {
        ArgumentNullException.ThrowIfNull(wider);

        lock (this.gate)
        {
            if (wider.MaximumRetrievedCharacters < this.bounds.MaximumRetrievedCharacters
                || wider.MaximumProviderCalls < this.bounds.MaximumProviderCalls
                || wider.MaximumTokens < this.bounds.MaximumTokens)
            {
                throw new ArgumentException("A run's ceilings are raised, never lowered.", nameof(wider));
            }

            if (this.raised)
            {
                return false;
            }

            this.raised = true;
            this.bounds = wider;
            this.RetrievalWasTruncated = false;

            return true;
        }
    }

    /// <summary>Gets whether a lookup, a listed page, or a census found mail this run's ceiling would not let it send, or a page of a message or of a file it carries was cut to what the run could still send.</summary>
    /// <remarks>The setter is written only from inside this type's lock, which is why it does not take one of its own; the getter does, because a reader outside the run holds nothing.</remarks>
    public bool RetrievalWasTruncated
    {
        get
        {
            lock (this.gate)
            {
                return field;
            }
        }

        private set;
    }

    /// <summary>Takes the passages of one lookup that still fit inside what this run may send.</summary>
    /// <param name="found">What the lookup found, in the order retrieval ranked them.</param>
    /// <returns>The leading passages that fit, which is every one of them until the ceiling is approached and none once it is reached.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="found" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Whole passages rather than a cut across the last one. An extract already cut to the per-passage bound is a
    /// readable piece of a message, and cutting it again to fill the remaining allowance exactly would hand the model a
    /// sentence ending mid-word for the sake of a few hundred characters.
    /// </remarks>
    public IReadOnlyList<EmailKnowledgePassage> AdmitPassages(IReadOnlyList<EmailKnowledgePassage> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return this.Admit(found, static passage => passage.StoredEmailId, RetrievedCharactersOf);
    }

    /// <summary>Takes the summaries of one listed page that still fit inside what this run may send.</summary>
    /// <param name="listed">The page, in the timeline order it was read in.</param>
    /// <returns>The leading summaries that fit, which is every one of them until the ceiling is approached and none once it is reached.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="listed" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A listing spends the same allowance a lookup does rather than one of its own, because both hand a provider mail
    /// somebody else wrote, and a run that could page through a mailbox beside the ceiling on searching it would have
    /// no ceiling at all. What a summary spends is what its author wrote: the subject and the sender. Its dates, flags,
    /// and identity are this deployment's own and count for nothing, which is what lets a page of a hundred headers cost
    /// a fraction of one lookup.
    /// </remarks>
    public IReadOnlyList<EmailSummary> AdmitSummaries(IReadOnlyList<EmailSummary> listed)
    {
        ArgumentNullException.ThrowIfNull(listed);

        return this.Admit(listed, static summary => summary.StoredEmailId, ListedCharactersOf);
    }

    /// <summary>Takes the groups of one census reading that still fit inside what this run may send.</summary>
    /// <param name="counted">The groups, in the order the census leads with them.</param>
    /// <returns>The leading groups that fit, which is every one of them until the ceiling is approached and none once it is reached.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="counted" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A census spends the same allowance for the reason a listing does: a sender's address and its domain are what
    /// somebody else wrote, and a run that could count beside the ceiling on reading would hand a provider text without
    /// one. A group spends its key, whatever the grouping — an address, a domain, a category's name, or a period's date —
    /// because each is text the provider is handed, and its figure spends nothing. A group names no message, so a census
    /// adds nothing to how many messages the run read.
    /// </remarks>
    public IReadOnlyList<MailCensusGroup> AdmitCensusGroups(IReadOnlyList<MailCensusGroup> counted)
    {
        ArgumentNullException.ThrowIfNull(counted);

        return this.Admit(counted, static _ => null, static group => group.Key?.Length ?? 0);
    }

    private List<T> Admit<T>(IReadOnlyList<T> found, Func<T, StoredEmailId?> messageOf, Func<T, int> charactersOf)
    {
        lock (this.gate)
        {
            List<T> admitted = [];

            foreach (var item in found)
            {
                var characters = charactersOf(item);

                if (this.retrievedCharacters + characters > this.bounds.MaximumRetrievedCharacters)
                {
                    // Stopping at the first item that does not fit rather than searching the rest for a smaller one:
                    // the items arrived in relevance or timeline order, and skipping ahead would silently prefer short
                    // messages to the ones that order put first the moment a run approached its ceiling.
                    this.RetrievalWasTruncated = true;

                    break;
                }

                this.retrievedCharacters += characters;

                if (messageOf(item) is { } message)
                {
                    this.retrievedMessages.Add(message.Value);
                }

                admitted.Add(item);
            }

            return admitted;
        }
    }

    /// <summary>Takes as much of one page of a message or of a file it carries as this run may still send.</summary>
    /// <param name="message">The message the page is read from, which the run then counts among the messages it read.</param>
    /// <param name="characters">How many characters the page holds.</param>
    /// <returns>How many of them the run may send: all of them while the allowance lasts, what is left of it once it is approached, and none once it is spent.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="characters" /> is negative.</exception>
    /// <remarks>
    /// A page is cut to what is left rather than refused whole, unlike a passage. A passage is a ranked guess at the
    /// place an answer stands, and dropping one leaves the next to try; a page is the continuation the model asked for
    /// by name, so the part of it the allowance covers is exactly what it would read first. The caller states the cut,
    /// and the run is recorded as truncated, so an answer resting on the page says it covered less than the whole.
    /// </remarks>
    public int AdmitPage(StoredEmailId message, int characters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characters);

        lock (this.gate)
        {
            var granted = Math.Min(characters, Math.Max(0, this.bounds.MaximumRetrievedCharacters - this.retrievedCharacters));

            if (granted < characters)
            {
                this.RetrievalWasTruncated = true;
            }

            if (granted > 0)
            {
                this.retrievedCharacters += granted;
                this.retrievedMessages.Add(message.Value);
            }

            return granted;
        }
    }

    /// <summary>Counts what one passage spends of the run's allowance: its extract, what its files contributed, and the sender written beside it.</summary>
    /// <remarks>
    /// The sender counts because it is mail the passage hands a provider exactly as the extract is, written by whoever
    /// wrote the message; so does every attachment's extract and the name its sender gave the file, which reach the same
    /// provider in the same envelope. The identity and the coordinates beside them are this deployment's own and count
    /// for nothing.
    /// </remarks>
    private static int RetrievedCharactersOf(EmailKnowledgePassage passage) =>
        passage.Text.Length
        + (passage.SenderDisplayName?.Length ?? 0)
        + (passage.SenderAddress?.Length ?? 0)
        + passage.AttachmentExtracts.Sum(static extract => extract.Text.Length + (extract.FileName?.Length ?? 0));

    private static int ListedCharactersOf(EmailSummary summary) =>
        (summary.Subject?.Length ?? 0) + (summary.SenderDisplayName?.Length ?? 0) + (summary.SenderAddress?.Length ?? 0);

    /// <summary>Takes this run's allowance for one more provider call.</summary>
    /// <exception cref="MailAnsweringBudgetExhaustedException">Thrown when the run has made every call it may make, or has consumed every token it may consume.</exception>
    public void RequireAllowanceForNextCall()
    {
        lock (this.gate)
        {
            if (this.providerCalls >= this.bounds.MaximumProviderCalls || this.tokens >= this.bounds.MaximumTokens)
            {
                throw MailAnsweringBudgetExhaustedException.RunSpent();
            }

            this.providerCalls++;
        }
    }

    /// <summary>Adds what one call consumed to this run's total.</summary>
    /// <param name="usage">The tokens the call sent and received.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="usage" /> is <see langword="null" />.</exception>
    public void RecordSpend(ChatTokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        lock (this.gate)
        {
            var spent = usage.InputTokens + usage.OutputTokens;

            this.tokens += spent;
            this.costliestCall = Math.Max(this.costliestCall, spent);
        }
    }

    /// <summary>Reads what this run has consumed so far.</summary>
    /// <returns>The counts, taken together so no reader sees two of them from different moments.</returns>
    /// <remarks>
    /// Safe to call while the run is executing, which is what a streamed run publishes during a run rather than only at
    /// the end of one. It is a snapshot rather than a view: a run that spends more after it was read has not changed
    /// what was published, which is the honest reading of a figure that arrived at a moment.
    /// </remarks>
    public MailAnsweringRunSpend Read()
    {
        lock (this.gate)
        {
            return new MailAnsweringRunSpend(
                this.providerCalls,
                this.tokens,
                this.retrievedCharacters,
                this.retrievedMessages.Count);
        }
    }
}
