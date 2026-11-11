// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>The strict JSON schema a passage rewriting's answer is asked to be a document of.</summary>
/// <remarks>
/// It follows <see cref="PassageRewriteDocument" />: one array of strings. How many strings it holds is not stated,
/// because strict mode drops the keyword that would state it; <see cref="PassageRewriteReading" /> holds the answer to
/// the number of passages the turn asked about, as it does for a model asked without the schema.
/// </remarks>
internal static class PassageRewriteSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "passage_rewrite";

    private const string Description = "The rewritten passages of a draft, one string for each numbered passage, in number order.";

    /// <summary>Builds the response format.</summary>
    /// <returns>The strict schema's response format.</returns>
    internal static ChatResponseFormatJson ResponseFormat() => StrictJsonSchema.ResponseFormat(
        StrictJsonSchema.ObjectOf(new JsonObject { ["passages"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.StringType()) }),
        Name,
        Description);
}
