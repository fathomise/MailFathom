// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;

namespace MailFathom.AI.SpamClassification;

/// <summary>Asks nothing, on a deployment that declares no decision model for spam classification.</summary>
/// <remarks>
/// A registration rather than an absent one, for the reason <see cref="Enrichment.InactiveEmailEnricher" /> gives: the
/// stage needs a reason it can act on and count. Nothing is read and nothing is sent, so an instance in this state
/// cannot disclose a message however often it is asked.
/// </remarks>
internal sealed class InactiveSpamModelJudge : ISpamModelJudge
{
    /// <summary>The one instance, since it holds nothing and answers everything the same way.</summary>
    internal static readonly InactiveSpamModelJudge Instance = new();

    private InactiveSpamModelJudge()
    {
    }

    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public Task<SpamModelJudgement> JudgeAsync(
        SpamModelEvidence evidence,
        SpamModelJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(SpamModelJudgement.Unanswered(SpamModelOutcome.NotActivated));
    }
}
