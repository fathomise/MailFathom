// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>The shape a passage-rewriting agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes a rewriting: what a model wrote is read into this and then held by
/// <see cref="PassageRewriteReading" /> to the number of passages the turn asked about.
/// </remarks>
internal sealed record PassageRewriteDocument
{
    /// <summary>Gets one rewritten text for each passage, in the order the turn numbered them.</summary>
    [JsonPropertyName("passages")]
    public IReadOnlyList<string?>? Passages { get; init; }
}
