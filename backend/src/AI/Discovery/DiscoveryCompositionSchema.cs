// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Discovery;

/// <summary>The strict JSON schema one composition's answer is asked to be a document of, built for the sources the run declared.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="DiscoveryResultDocument" />: the answer, its confidence as one of the three bands, the sources
/// it rests on, and the material a conflict, a timeline, or a comparison is filled from. Every cited source is an
/// <c>enum</c> of the names the turn minted, so a claim resting on a name nobody offered is an answer a provider honouring
/// the schema cannot produce; a turn that declared no source cites a plain string, which the reading resolves to
/// nothing. A column is an <c>enum</c> of the catalogue <see cref="FactTableColumn" /> holds. Every array is written,
/// and one the turn did not ask for is empty; a cell the correspondence says nothing about has a <c>null</c> value.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so what it cannot state stays the reading's, and
/// <see cref="DiscoveryCompositionReading" /> refuses none of it — an empty <c>sources</c> is the answer that the mail
/// does not answer, by design. An answer past <see cref="PresentationText.MaxLength" /> is cut. Citations, sides,
/// events, columns, and rows past their bounds are cut to the leading ones, and a column named twice is kept once. An
/// event whose date does not parse or whose summary or subject is empty or too long, a side whose statement is, a row
/// whose cells do not match the columns, and a cell value that is empty or too long are each dropped on their own,
/// because a repaired one would state what the model did not write.
/// </para>
/// </remarks>
internal static class DiscoveryCompositionSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "discovery_result";

    private const string Description = "What the extracts of one mailbox say in reply to a question, and the sources each part rests on.";

    /// <summary>The bands a model reports its confidence in, in the words the reading recognizes.</summary>
    private static readonly string[] ConfidenceBands = ["high", "moderate", "low"];

    /// <summary>Builds the response format for one composition.</summary>
    /// <param name="sources">The sources the run declared, whose minted names are the only ones a claim may cite.</param>
    /// <returns>The strict schema's response format.</returns>
    internal static ChatResponseFormatJson For(IReadOnlyList<DiscoveryComposedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var cited = sources.Count is 0
            ? StrictJsonSchema.StringType()
            : StrictJsonSchema.Enumeration(sources.Select(static source => source.Citation.Id.Value));

        var schema = StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["answer"] = StrictJsonSchema.StringType(),
            ["confidence"] = StrictJsonSchema.Enumeration(ConfidenceBands),
            ["sources"] = StrictJsonSchema.ArrayOf(cited.DeepClone()),
            ["conflict"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["statement"] = StrictJsonSchema.StringType(),
                ["sources"] = StrictJsonSchema.ArrayOf(cited.DeepClone()),
            })),
            ["events"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["occurredAt"] = StrictJsonSchema.StringType(),
                ["summary"] = StrictJsonSchema.StringType(),
                ["subject"] = StrictJsonSchema.StringType(),
                ["sources"] = StrictJsonSchema.ArrayOf(cited.DeepClone()),
            })),
            ["columns"] = StrictJsonSchema.ArrayOf(
                StrictJsonSchema.Enumeration(FactTableColumn.All.Select(static column => column.Identity))),
            ["rows"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["cells"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
                {
                    ["value"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                    ["sources"] = StrictJsonSchema.ArrayOf(cited),
                })),
            })),
        });

        return StrictJsonSchema.ResponseFormat(schema, Name, Description);
    }
}
