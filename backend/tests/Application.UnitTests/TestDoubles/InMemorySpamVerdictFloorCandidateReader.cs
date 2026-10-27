// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Floor;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds the candidates a test stored, and answers them in identity order without applying the query's rule.</summary>
/// <remarks>
/// The query's own selection is PostgreSQL's to prove. Answering every stored candidate is what lets a test reach the
/// rule the pass decides for itself, and the conditional write the store makes, with candidates a query would have left out.
/// </remarks>
internal sealed class InMemorySpamVerdictFloorCandidateReader : ISpamVerdictFloorCandidateReader
{
    private readonly List<(MailAccountId Account, SpamVerdictFloorCandidate Candidate)> candidates = [];

    /// <summary>Gets the terms each read was asked under, oldest first.</summary>
    internal List<SpamVerdictFloorTerms> RequestedTerms { get; } = [];

    /// <summary>Gets the position each read resumed after, oldest first.</summary>
    internal List<StoredEmailId?> RequestedResumptions { get; } = [];

    /// <summary>Stores one candidate of one account.</summary>
    /// <param name="account">The account it belongs to.</param>
    /// <param name="candidate">The candidate.</param>
    internal void Add(MailAccountId account, SpamVerdictFloorCandidate candidate) =>
        this.candidates.Add((account, candidate));

    /// <inheritdoc />
    public Task<IReadOnlyList<SpamVerdictFloorCandidate>> GetCandidatesAsync(
        MailAccountId account,
        SpamVerdictFloorTerms terms,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        this.RequestedTerms.Add(terms);
        this.RequestedResumptions.Add(resumeAfter);

        IReadOnlyList<SpamVerdictFloorCandidate> batch =
        [
            .. this.candidates
                .Where(stored => stored.Account == account)
                .Select(static stored => stored.Candidate)
                .Where(candidate => resumeAfter is not { } position || candidate.Id.Value.CompareTo(position.Value) > 0)
                .OrderBy(static candidate => candidate.Id.Value)
                .Take(batchSize),
        ];

        return Task.FromResult(batch);
    }
}
