// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.AI.Decisions;

/// <summary>What one decision request is sent with, beyond its input and its questions.</summary>
/// <remarks>Shaped after <see cref="ChatOptions" />: everything is optional, and a client fills what the caller left unset from its own defaults.</remarks>
internal sealed class DecisionOptions
{
    /// <summary>Gets or sets the model the request asks for, or <see langword="null" /> for the client's default.</summary>
    public string? ModelId { get; set; }

    /// <summary>Gets or sets values a middleware or an adapter reads that no member above names.</summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

    /// <summary>Copies these options, so a middleware can change what it passes on without changing what its caller holds.</summary>
    /// <returns>The copy.</returns>
    public DecisionOptions Clone() => new()
    {
        ModelId = this.ModelId,
        AdditionalProperties = this.AdditionalProperties?.Clone(),
    };
}
