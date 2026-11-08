// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Retrieval;

namespace MailFathom.Application.Discovery.Planning;

/// <summary>Derives what one question decided, from the question and the scope it was asked within.</summary>
/// <remarks>
/// The port a run reaches the model through, and the whole of what a provider decides about a Discover run. It answers
/// with a plan rather than with a result, so nothing provider-shaped travels beyond this boundary and a deployment can
/// change chat provider without changing what a client can render.
/// </remarks>
public interface IDiscoveryRunPlanner
{
    /// <summary>Reads a question and answers with the retrieval and the composition it implies.</summary>
    /// <param name="question">The question and the scope bounding what may be read to answer it.</param>
    /// <param name="cancellationToken">Cancels the derivation.</param>
    /// <returns>The plan.</returns>
    /// <remarks>
    /// A question this derivation cannot read as one of the named kinds still produces a plan, carrying
    /// <see cref="DiscoveryIntent.Unclassified" /> and a lookup drawn from the question itself. A run is refused only
    /// where the deployment cannot answer questions at all, which the caller establishes before reaching this port.
    /// </remarks>
    Task<DiscoveryRunPlan> DerivePlanAsync(MailQuestion question, CancellationToken cancellationToken);

    /// <summary>Reads what the plan's own lookups found and answers with the lookups still worth running, or with none where it found enough.</summary>
    /// <param name="question">The question and the scope bounding what may be read to answer it.</param>
    /// <param name="plan">The plan the run followed, whose lookups have run.</param>
    /// <param name="evidence">What those lookups found.</param>
    /// <param name="cancellationToken">Cancels the follow-up.</param>
    /// <returns>
    /// At most <see cref="RetrievalPlan.MaximumFollowUpLookups" /> further lookups, none repeating one the plan ran; empty
    /// where the extracts already answer the question or where no follow-up could be read.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The round may only add lookups. It answers with no intent, so it cannot change what the result is composed of, and
    /// with no scope, so every lookup it adds runs inside the scope the question was asked within, exactly as the plan's
    /// own do.
    /// </para>
    /// <para>
    /// An answer that cannot be read, a provider that fails, and a turn too large to send each end the round with no
    /// lookup rather than ending the run, because the run already holds what the plan found. A spend ceiling is the
    /// exception, as it is for <see cref="DerivePlanAsync" />: a call the run's or the period's ledger refuses never
    /// leaves, and the refusal ends the run stating which ceiling stopped it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<EmailKnowledgeQuery>> FollowUpAsync(
        MailQuestion question,
        DiscoveryRunPlan plan,
        DiscoveryEvidence evidence,
        CancellationToken cancellationToken);
}
