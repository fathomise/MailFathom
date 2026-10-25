// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.AI.Decisions;

/// <summary>Names which provider, at which address, a decision client reaches, and which model it asks by default.</summary>
/// <remarks>Shaped after <see cref="ChatClientMetadata" /> and reached the same way, through <see cref="IDecisionClient.GetService" />.</remarks>
/// <param name="providerName">The provider's name, as telemetry reports it.</param>
/// <param name="providerUri">The address requests are sent to.</param>
/// <param name="defaultModelId">The model a request asks for when its options name none.</param>
public sealed class DecisionClientMetadata(string? providerName = null, Uri? providerUri = null, string? defaultModelId = null)
{
    /// <summary>Gets the provider's name, as telemetry reports it.</summary>
    public string? ProviderName { get; } = providerName;

    /// <summary>Gets the address requests are sent to.</summary>
    public Uri? ProviderUri { get; } = providerUri;

    /// <summary>Gets the model a request asks for when its options name none.</summary>
    public string? DefaultModelId { get; } = defaultModelId;
}
