// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;

namespace MailFathom.IntegrationTests.ProviderAdapters;

/// <summary>Reads what a decision provider-contract run is pointed at from the environment the workflow supplies.</summary>
internal static class DecisionProviderContractSettings
{
    private const string ApiKeyVariable = "MAILFATHOM_DECISION_API_KEY";
    private const string AddressVariable = "MAILFATHOM_DECISION_ADDRESS";
    private const string ModelVariable = "MAILFATHOM_DECISION_MODEL";

    /// <summary>Builds the plan a contract run asks the provider with.</summary>
    /// <returns>The plan.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the run was asked for without a model.</exception>
    /// <remarks>An empty address is the global router, which is what a deployment that wrote none reaches.</remarks>
    public static DecisionModelPlan Plan()
    {
        var address = Environment.GetEnvironmentVariable(AddressVariable);

        return new DecisionModelPlan(
            "contract",
            address is { Length: > 0 } ? new Uri(address, UriKind.Absolute) : null,
            AiProviderContractRun.Required(ModelVariable),
            RequestTimeout: TimeSpan.FromSeconds(60),
            MaximumRequestCharacters: 8000);
    }

    /// <summary>Reads the provider key a contract run authenticates with.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the run was asked for without one.</exception>
    public static string ApiKey() => AiProviderContractRun.Required(ApiKeyVariable);
}
