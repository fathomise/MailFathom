// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Enrichment;

/// <summary>The strict JSON schema one enrichment's answer is asked to be a document of, built for the passages its turn published.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="EmailEnrichmentDocument" />: three readings, each a nullable object citing passages by the
/// positions the turn gave them, and the tasks the message asks of the person. A cited position is an <c>enum</c> of
/// the message's own passage positions, so a reading resting on a passage the message does not hold is an answer a
/// provider honouring the schema cannot produce. A reading written as <c>null</c> is a reading left out, and an object
/// whose three readings are null and whose tasks are empty is the derivation of nothing to say.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so no bound is stated here, and
/// <see cref="EmailEnrichmentReading" /> decides each one as it does without the schema. A reading's text runs long and
/// is cut at its bound; more passages than a reading cites are cut to the leading ones; more tasks than a message
/// proposes are cut to the leading ones; a <c>dueAt</c> or a <c>dueOn</c> that is no date falls away on its own and
/// keeps the reading or the task. What it still drops is a reading with no words, no reason, or no passage, and a task
/// with no title: a reading nobody can check against the message, and a task that names nothing to do, neither of which
/// has an honest repair — and dropping one leaves every other reading of the answer standing.
/// </para>
/// </remarks>
internal static class EmailEnrichmentSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "email_enrichment";

    private const string Description = "Readings of one email: what it is about, why it is pressing, what it commits somebody to, and what it asks of the person.";

    /// <summary>Builds the response format for one message.</summary>
    /// <param name="passageCount">How many passages the turn published, which a reading cites by position.</param>
    /// <returns>The strict schema's response format.</returns>
    internal static ChatResponseFormatJson For(int passageCount)
    {
        var schema = StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["sense"] = StrictJsonSchema.Nullable(StrictJsonSchema.Reference("reading")),
            ["significance"] = StrictJsonSchema.Nullable(StrictJsonSchema.Reference("reading")),
            ["commitment"] = StrictJsonSchema.Nullable(StrictJsonSchema.Reference("commitment")),
            ["tasks"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["title"] = StrictJsonSchema.StringType(),
                ["dueOn"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
            })),
        });

        var passages = StrictJsonSchema.ArrayOf(StrictJsonSchema.PositionOf(passageCount));

        schema["$defs"] = new JsonObject
        {
            ["reading"] = StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["text"] = StrictJsonSchema.StringType(),
                ["reason"] = StrictJsonSchema.StringType(),
                ["passages"] = passages.DeepClone(),
            }),
            ["commitment"] = StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["text"] = StrictJsonSchema.StringType(),
                ["reason"] = StrictJsonSchema.StringType(),
                ["passages"] = passages,
                ["dueAt"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
            }),
        };

        return StrictJsonSchema.ResponseFormat(schema, Name, Description);
    }
}
