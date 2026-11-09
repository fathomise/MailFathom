// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;

namespace MailFathom.AI.Discovery;

/// <summary>Turns what a planning agent wrote into a plan the rest of the system can run, or into a plan of last resort.</summary>
/// <remarks>
/// <para>
/// <strong>It never fails.</strong> A question is not unanswerable because a model wrapped its answer in prose, named
/// an intent nobody defined, or proposed a lookup whose words are too long to search for. Each of those is read as far
/// as it goes and the rest is replaced: an answer that is no plan at all by <see cref="Fallback" />, which asks the
/// question's own words over the whole scope — the plan a system with no model at all would run — and a plan left with
/// no lookup by those same words beside the intent and the enough it did name.
/// </para>
/// <para>
/// That is also what makes the derivation testable without a provider: every reading below is a pure function of the
/// text, so the cases a provider produces once in a thousand runs are ordinary examples here.
/// </para>
/// </remarks>
internal static class DiscoveryPlanReading
{
    /// <summary>Reads a plan out of an agent's answer, falling back to the question's own words wherever the answer cannot be believed.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="question">The question, whose words are the lookup of last resort.</param>
    /// <param name="retrievalBounds">What this deployment's retrieval returns at most.</param>
    /// <param name="askedAt">The instant the turn stated, whose offset every written received bound belongs to.</param>
    /// <returns>The plan, and whether it was read from the answer or fell back.</returns>
    internal static DiscoveryPlanReadingOutcome Read(
        string? answerText,
        MailQuestionText question,
        EmailKnowledgeBounds retrievalBounds,
        DateTimeOffset askedAt)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(retrievalBounds);

        if (ReadDocument(answerText) is not { } document)
        {
            return new DiscoveryPlanReadingOutcome(Fallback(question, retrievalBounds), WasRead: false);
        }

        IReadOnlyList<EmailKnowledgeQuery> proposed =
        [
            .. (document.Lookups ?? [])
                .Where(static lookup => lookup is not null)
                .Select(lookup => ToQuery(lookup, askedAt))
                .OfType<EmailKnowledgeQuery>()
                .Take(RetrievalPlan.MaximumLookups),
        ];

        // A plan whose lookups all fell away still classified the question and judged its size, and a strict schema admits
        // an empty array, so the question's own words stand in for the lookups rather than the whole plan being refused.
        IReadOnlyList<EmailKnowledgeQuery> lookups = proposed.Count is 0 ? [QuestionWords(question)] : proposed;

        // An intent this catalogue does not hold is the ordinary case the product names rather than a defect: a question
        // that is none of the four named kinds is still answered, opening with an answer and the evidence behind it.
        var intent = DiscoveryIntent.TryParse(document.Intent, out var named) ? named : DiscoveryIntent.Unclassified;

        var plan = DiscoveryRunPlan.Compose(
            intent,
            RetrievalPlan.Create(
                retrievalBounds,
                lookups,
                Bounded(document.SufficientPassages, retrievalBounds)));

        return new DiscoveryPlanReadingOutcome(plan, WasRead: true);
    }

    /// <summary>Reads the lookups a follow-up answer adds, or none wherever the answer cannot be believed.</summary>
    /// <param name="answerText">What the agent wrote in the follow-up turn.</param>
    /// <param name="plan">The plan whose lookups already ran, which a follow-up lookup may not repeat.</param>
    /// <param name="askedAt">The instant the turn stated, whose offset every written received bound belongs to.</param>
    /// <returns>At most <see cref="RetrievalPlan.MaximumFollowUpLookups" /> lookups, each read as a plan's lookup is read.</returns>
    /// <remarks>
    /// Only the lookups are read. An intent or an enough the answer also carries is ignored rather than compared, because
    /// the round may add searching and change nothing else, and an answer that is no plan at all adds nothing: the run
    /// already holds what the plan found, so there is no fallback to fall back to.
    /// </remarks>
    internal static IReadOnlyList<EmailKnowledgeQuery> ReadFollowUp(string? answerText, DiscoveryRunPlan plan, DateTimeOffset askedAt)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var alreadyRun = plan.Retrieval.Lookups.Select(static lookup => lookup.QueryText).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ReadDocument(answerText) is not { } document
            ? []
            :
            [
                .. (document.Lookups ?? [])
                    .Where(static lookup => lookup is not null)
                    .Select(lookup => ToQuery(lookup, askedAt))
                    .OfType<EmailKnowledgeQuery>()
                    .Where(lookup => !alreadyRun.Contains(lookup.QueryText))
                    .DistinctBy(static lookup => lookup.QueryText, StringComparer.OrdinalIgnoreCase)
                    .Take(RetrievalPlan.MaximumFollowUpLookups),
            ];
    }

    /// <summary>Composes the plan a run falls back to: the question's own words, over everything its scope admits.</summary>
    /// <param name="question">The question.</param>
    /// <param name="retrievalBounds">What this deployment's retrieval returns at most.</param>
    /// <returns>The plan.</returns>
    /// <remarks>
    /// The question text is what a person wrote rather than the words mail would carry, so this ranks worse than a
    /// derived lookup — which is the point: it is what the run does when nothing better was derived, and it is bounded
    /// by the same scope everything else is.
    /// </remarks>
    internal static DiscoveryRunPlan Fallback(MailQuestionText question, EmailKnowledgeBounds retrievalBounds)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(retrievalBounds);

        return DiscoveryRunPlan.Compose(
            DiscoveryIntent.Unclassified,
            RetrievalPlan.Create(
                retrievalBounds,
                [QuestionWords(question)],
                retrievalBounds.MaximumPassages));
    }

    private static EmailKnowledgeQuery QuestionWords(MailQuestionText question) =>
        EmailKnowledgeQuery.ForText(Truncated(question.Value));

    private static DiscoveryPlanDocument? ReadDocument(string? answerText) =>
        AgentJsonAnswer.Read(answerText, DiscoveryPlanJsonContext.Default.DiscoveryPlanDocument);

    /// <summary>Turns one proposed lookup into a query, or into nothing where its words could not be searched for.</summary>
    /// <remarks>
    /// <para>
    /// Only the query text decides whether a lookup survives, because it is the only part without which there is
    /// nothing to rank. A filter the deployment refuses — an address that is not one, a range that ends before it
    /// starts — is left in place and skips its own lookup at retrieval, where the refusal names which filter it was.
    /// </para>
    /// <para>
    /// A received bound is read against the anchor rather than bound from the document, so <em>this week</em> resolved
    /// by the model reaches retrieval as the week the asking person is actually in. One it wrote in some other form is
    /// dropped rather than guessed at, which widens that lookup and hides no mail — the opposite of reading it in a
    /// zone nobody named, which narrows it onto the wrong hours while looking exactly like a bound that worked.
    /// </para>
    /// </remarks>
    private static EmailKnowledgeQuery? ToQuery(DiscoveryLookupDocument lookup, DateTimeOffset askedAt)
    {
        if (string.IsNullOrWhiteSpace(lookup.QueryText))
        {
            return null;
        }

        var queryText = lookup.QueryText.Trim();

        return queryText.Length > EmailSearchQueryText.MaximumLength
            ? null
            : new EmailKnowledgeQuery
            {
                QueryText = queryText,
                SenderAddress = lookup.SenderAddress,
                RecipientAddress = lookup.RecipientAddress,
                SubjectFragment = lookup.SubjectFragment,
                ReceivedOnOrAfter = AnchoredInstant.Read(lookup.ReceivedOnOrAfter, askedAt),
                ReceivedBefore = AnchoredInstant.Read(lookup.ReceivedBefore, askedAt),
                IsMarkedSeen = lookup.IsMarkedSeen,
                IsFlagged = lookup.IsFlagged,
                Keyword = lookup.Keyword,
                HasAttachments = lookup.HasAttachments,
            };
    }

    /// <summary>Reads how much is enough, clamping rather than refusing whatever the model asked for.</summary>
    /// <remarks>
    /// A number outside the bound says the model misjudged the size of the question rather than that the plan is
    /// unusable, and the bound it is clamped to is the one retrieval would have applied anyway.
    /// </remarks>
    private static int Bounded(int? sufficientPassages, EmailKnowledgeBounds retrievalBounds) =>
        sufficientPassages is { } asked
            ? Math.Clamp(asked, 1, retrievalBounds.MaximumPassages)
            : retrievalBounds.MaximumPassages;

    /// <summary>Cuts a question down to what a query may be, without splitting a character in half.</summary>
    /// <remarks>
    /// A question may be twice the length a query is allowed to be. The surrogate check is the same one the passage
    /// bound applies and for the same reason: a cut between the halves of an astral character produces text no
    /// comparison can be made against.
    /// </remarks>
    private static string Truncated(string question)
    {
        const int limit = EmailSearchQueryText.MaximumLength;

        if (question.Length <= limit)
        {
            return question;
        }

        return question[..(char.IsLowSurrogate(question[limit]) ? limit - 1 : limit)];
    }
}
