// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.AI.BodyCleanup;

/// <summary>Reads a body-cleanup agent's answer without reflection, as everything serialized in this repository is.</summary>
/// <remarks>
/// <para>
/// Forgiving about case, a comment, and a trailing comma, because the text was written by a model rather than by a
/// program — and about nothing else.
/// </para>
/// <para>
/// A member nothing here declares is ignored, as it is in every other answer read in this product, which is what
/// <see cref="Orchestration.AgentJsonAnswer" /> holds for all of them. That keeps this rendering's promise that no word of
/// it came from a model, because the promise rests on what is read rather than on what is refused: a range carries two
/// block numbers and a word from a fixed vocabulary, so a block's text written beside its range, a reason, or a rewritten
/// line has nowhere to be read into and never reaches the reader.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(MailBodyCleanupDocument))]
internal sealed partial class MailBodyCleanupJsonContext : JsonSerializerContext;
