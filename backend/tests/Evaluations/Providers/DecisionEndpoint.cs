// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Evaluations.Providers;

/// <summary>The one endpoint a run reaches its decision models at, apart from the chat endpoint.</summary>
/// <remarks>
/// <para>
/// Apart because a decision model speaks a protocol of its own, ADR 0041's, and a deployment declares it in a section of
/// its own with a credential of its own. The variables are the ones the decision provider-contract tests read, so a
/// repository configured for those runs this as it stands.
/// </para>
/// <para>
/// The address is optional because the global router is a valid answer; the key is not, and a requested run without one
/// fails naming it rather than skipping. The model is read by <see cref="EvaluationDeclaration" /> as the fallback where
/// the run's block names none.
/// </para>
/// </remarks>
internal static class DecisionEndpoint
{
    /// <summary>The variable carrying the host every decision request goes to.</summary>
    public const string AddressVariable = "MAILFATHOM_DECISION_ADDRESS";

    /// <summary>The variable carrying the key every decision request authenticates with.</summary>
    public const string ApiKeyVariable = "MAILFATHOM_DECISION_API_KEY";

    /// <summary>The variable carrying the decision model measured where the run's block names none.</summary>
    public const string ModelVariable = "MAILFATHOM_DECISION_MODEL";

    /// <summary>Reads where decision requests go.</summary>
    /// <returns>The address, or <see langword="null" /> where the run left the global router.</returns>
    public static Uri? Address() =>
        AiEvaluationRun.Optional(AddressVariable) is { } address ? new Uri(address, UriKind.Absolute) : null;

    /// <summary>Reads the key every decision request is authenticated with.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was asked for without one.</exception>
    public static string ApiKey() => AiEvaluationRun.Required(ApiKeyVariable);
}
