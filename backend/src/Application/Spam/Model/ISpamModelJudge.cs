// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Spam.Model;

/// <summary>Asks a decision model about one message's evidence, for one of the model stage's two jobs.</summary>
/// <remarks>
/// <para>
/// A port, because the model is reached through the decision client the AI boundary composes, and nothing of it — the
/// questions, the options, the provider — crosses into this layer. What comes back is a closed answer read into the
/// domain's own values, or the reason there is none.
/// </para>
/// <para>
/// It never fails the classification. Every way the call can go wrong is an outcome rather than an exception, because
/// the stage is fail-open: whatever the model could not answer, the deterministic result stands.
/// </para>
/// </remarks>
public interface ISpamModelJudge
{
    /// <summary>Gets whether a decision model is declared for spam classification at all.</summary>
    bool IsActive { get; }

    /// <summary>Asks the model about one message.</summary>
    /// <param name="evidence">What the model reads, already minimised.</param>
    /// <param name="job">Whether the model decides the verdict or only names the subcategory of one already decided.</param>
    /// <param name="cancellationToken">Withdraws the call.</param>
    /// <returns>The answer, or the reason there is none.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller withdrew the call.</exception>
    Task<SpamModelJudgement> JudgeAsync(
        SpamModelEvidence evidence,
        SpamModelJob job,
        CancellationToken cancellationToken);
}
