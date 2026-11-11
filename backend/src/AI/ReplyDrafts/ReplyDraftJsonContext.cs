// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>Reads a reply-drafting or a passage-rewriting agent's answer without reflection, as everything serialized in this repository is.</summary>
/// <remarks>
/// The options are deliberately forgiving about case, about a field nobody declared, and about a number written as a
/// string, as in <c>"messages": ["0"]</c>, because the text being read was written by a model rather than by a program.
/// What they are not forgiving about is what the values mean: that is <see cref="ReplyDraftReading" />'s to decide
/// against the draft's own bounds.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ReplyDraftDocument))]
[JsonSerializable(typeof(PassageRewriteDocument))]
internal sealed partial class ReplyDraftJsonContext : JsonSerializerContext;
