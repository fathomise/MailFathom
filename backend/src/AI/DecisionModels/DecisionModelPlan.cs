// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.AI.DecisionModels;

/// <summary>The validated declaration of one decision model: where it is reached, what it is asked for, and the bounds one request is held to.</summary>
/// <param name="Alias">The deployment's own name for the model, which keys its credential, its resilience circuit, its health, and every log line about it.</param>
/// <param name="Address">The address the model is reached at, or <see langword="null" /> for the adapter's own default.</param>
/// <param name="Model">The model asked for, as a dated build or a versioned identifier rather than an alias that moves between builds.</param>
/// <param name="RequestTimeout">How long one attempt may take before it is abandoned.</param>
/// <param name="MaximumRequestCharacters">The most characters one request's input and questions may carry together.</param>
/// <remarks>
/// Carries no fallback, and that is the decision rather than an omission: a threshold is calibrated against one build,
/// so a second model's probability judged against it would be a score nobody calibrated. A failure the retries do not
/// cure is reported to the caller, which keeps whatever it had without the model.
/// </remarks>
public sealed record DecisionModelPlan(
    string Alias,
    Uri? Address,
    string Model,
    TimeSpan RequestTimeout,
    int MaximumRequestCharacters);
