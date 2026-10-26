// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers every judgement with the one it was given, and keeps what it was asked.</summary>
/// <param name="judgement">The judgement every call answers with.</param>
/// <param name="isActive">Whether the judge reports a model to consult.</param>
internal sealed class ScriptedSpamModelJudge(SpamModelJudgement judgement, bool isActive = true) : ISpamModelJudge
{
    /// <summary>Gets every job the judge was asked to do, with the evidence it was shown, in call order.</summary>
    public List<(SpamModelEvidence Evidence, SpamModelJob Job)> Asked { get; } = [];

    /// <inheritdoc />
    public bool IsActive => isActive;

    /// <summary>Builds a judge no test expects to be asked.</summary>
    /// <returns>A judge whose every call is recorded and answered as not activated.</returns>
    public static ScriptedSpamModelJudge Unused() =>
        new(SpamModelJudgement.Unanswered(SpamModelOutcome.NotActivated));

    /// <inheritdoc />
    public Task<SpamModelJudgement> JudgeAsync(
        SpamModelEvidence evidence,
        SpamModelJob job,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Asked.Add((evidence, job));

        return Task.FromResult(judgement);
    }
}
