// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Judging;

/// <summary>The judges every scenario is graded by: one to three jurors, and an arbiter asked only where they do not all pass.</summary>
/// <remarks>
/// <para>
/// Several jurors from different providers, because no model under test should be graded by itself alone and one juror's
/// bias is outweighed by the others. An arbiter, because a stronger model asked about every verdict costs several times
/// what the rest of a run does, while the verdicts the jurors all pass are the ones it would rarely overturn.
/// <see cref="JudgePanelEvaluator" /> holds the rule that decides between them.
/// </para>
/// <para>
/// The arbiter is optional. A run declaring none grades with the jurors alone and records on every rating whether it would
/// have been escalated, which is how the jurors are measured and the arbiter's cost estimated before anybody pays for it.
/// </para>
/// <para>
/// Every judge shares the run's endpoint, its key, and one reasoning effort. Each keeps a
/// <see cref="JudgeDeclaration.CachingKey" /> of its own, so declaring an arbiter later, or replacing one juror, asks only the
/// judge that changed.
/// </para>
/// </remarks>
internal sealed class JudgePanel
{
    /// <summary>The most jurors a run may declare, since every juror is asked about every rating.</summary>
    public const int MaximumJurors = 3;

    private const string JurorsVariable = "MAILFATHOM_JUDGE_MODELS";
    private const string ArbiterVariable = "MAILFATHOM_JUDGE_ARBITER_MODEL";
    private const string ReasoningEffortVariable = "MAILFATHOM_JUDGE_REASONING_EFFORT";

    private JudgePanel(IReadOnlyList<JudgeDeclaration> jurors, JudgeDeclaration? arbiter)
    {
        this.Jurors = jurors;
        this.Arbiter = arbiter;
    }

    /// <summary>Gets the jurors, in the order they were declared, each asked about every rating.</summary>
    public IReadOnlyList<JudgeDeclaration> Jurors { get; }

    /// <summary>Gets the arbiter asked about a rating the jurors do not all pass, or <see langword="null" /> where none is declared.</summary>
    public JudgeDeclaration? Arbiter { get; }

    /// <summary>Gets every judge on the panel, the jurors first and the arbiter last.</summary>
    public IEnumerable<JudgeDeclaration> Judges => this.Arbiter is null ? this.Jurors : [.. this.Jurors, this.Arbiter];

    /// <summary>Reads the panel a requested run was given.</summary>
    /// <returns>The panel.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable and never a value, when the run was asked for without jurors or the key, or with a panel <see cref="Of" /> refuses.</exception>
    public static JudgePanel Read() =>
        Of(
            EvaluationEndpoint.Address(),
            AiEvaluationRun.Required(JurorsVariable).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            AiEvaluationRun.Optional(ArbiterVariable),
            EvaluationEndpoint.ApiKey(),
            AiEvaluationRun.Optional(ReasoningEffortVariable));

    /// <summary>Declares a panel from its values.</summary>
    /// <param name="address">Where every judge is reached, or <see langword="null" /> for the provider's own address.</param>
    /// <param name="jurorModels">The jurors' models, one to <see cref="MaximumJurors" />, each named once.</param>
    /// <param name="arbiterModel">The arbiter's model, which no juror holds, or <see langword="null" /> for a panel of jurors alone.</param>
    /// <param name="apiKey">The key every judge's provider authenticates with.</param>
    /// <param name="reasoningEffort">How hard every judge reasons, or <see langword="null" /> to send nothing and leave each provider's default.</param>
    /// <returns>The panel.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable and never a value, when the jurors are too few, too many, or repeated, when the arbiter is also a juror, or when the effort is not shaped like a level.</exception>
    public static JudgePanel Of(Uri? address, IReadOnlyList<string> jurorModels, string? arbiterModel, string apiKey, string? reasoningEffort)
    {
        ArgumentNullException.ThrowIfNull(jurorModels);

        if (jurorModels.Count is 0 or > MaximumJurors)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{JurorsVariable} names {jurorModels.Count} juror(s); a panel takes one to {MaximumJurors}, separated by commas."));
        }

        if (jurorModels.Distinct(StringComparer.Ordinal).Count() != jurorModels.Count)
        {
            throw new InvalidOperationException($"{JurorsVariable} names one model more than once, which would only ask it twice.");
        }

        if (arbiterModel is not null && jurorModels.Contains(arbiterModel, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{ArbiterVariable} names a model {JurorsVariable} already seats as a juror, so it would overrule only itself.");
        }

        var jurors = jurorModels
            .Select((model, index) => JudgeDeclaration.Of(
                string.Create(CultureInfo.InvariantCulture, $"juror {index + 1}"),
                address,
                model,
                apiKey,
                reasoningEffort))
            .ToArray();

        return new(
            jurors,
            arbiterModel is null ? null : JudgeDeclaration.Of("arbiter", address, arbiterModel, apiKey, reasoningEffort));
    }

    /// <summary>Opens every judge on the panel behind the one client the run's evaluators are configured with.</summary>
    /// <param name="spend">What every verdict's tokens and charge are recorded against, whichever judge gave it.</param>
    /// <returns>The panel's client, which the caller disposes.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Present takes ownership of every provider's client, and the finally block disposes them only when Present never returned; the analyzer does not follow ownership through a method call.")]
    public JudgePanelChatClient Open(SpendMeter spend)
    {
        var opened = new List<IChatClient>();

        try
        {
            foreach (var judge in this.Judges)
            {
                opened.Add(judge.OpenProvider(spend));
            }

            var panel = this.Present(opened);
            opened.Clear();

            return panel;
        }
        finally
        {
            foreach (var client in opened)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>Puts each judge's declaration onto its own client, and the panel in front of them all.</summary>
    /// <param name="providers">One client per judge, in the order of <see cref="Judges" />, which the result takes ownership of.</param>
    /// <returns>The panel's client.</returns>
    /// <exception cref="ArgumentException">Thrown when the clients do not match the judges one for one.</exception>
    public JudgePanelChatClient Present(IReadOnlyList<IChatClient> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var judges = this.Judges.ToArray();

        if (providers.Count != judges.Length)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The panel seats {judges.Length} judge(s) and was handed {providers.Count} client(s)."),
                nameof(providers));
        }

        return new(this, judges.Zip(providers, static (judge, provider) => (judge.CachingKey, judge.Present(provider))));
    }
}
