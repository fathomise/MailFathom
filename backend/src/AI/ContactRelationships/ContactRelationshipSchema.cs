// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.ContactRelationships;

/// <summary>The strict JSON schema one relationship card's answer is asked to be a document of, built for the correspondence its turn numbered.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="ContactRelationshipDocument" />: five fields, each a nullable observation citing the
/// conversations and documents by the one run of numbers the turn gave them. A cited number is an <c>enum</c> of
/// exactly those numbers, so a line resting on something the correspondence does not hold is an answer a provider
/// honouring the schema cannot produce. A field written as <c>null</c> is one left out, and an answer whose note is
/// <c>null</c> is the empty card the prose instruction asks for as an empty object: no card at all, because the note is
/// what every other line is drawn around.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so no bound is stated here, and
/// <see cref="ContactRelationshipReading" /> decides each one as it does without the schema. A line's text runs long and
/// is cut at its bound, and more sources than a line cites are cut to the leading ones. What it still drops is a line
/// with no words or no source: a line nobody could follow back to the correspondence, which has no honest repair —
/// dropping one leaves the rest of the card standing, and only a note dropped this way leaves no card.
/// </para>
/// </remarks>
internal static class ContactRelationshipSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "contact_relationship";

    private const string Description = "Where a relationship with one person stands: a note, a next action, when they write, what is open, and which matters it concerns.";

    /// <summary>Builds the response format for one correspondence.</summary>
    /// <param name="sourceCount">How many conversations and documents the turn numbered together, which a line cites by number.</param>
    /// <returns>The strict schema's response format.</returns>
    internal static ChatResponseFormatJson For(int sourceCount)
    {
        var observation = StrictJsonSchema.Nullable(StrictJsonSchema.Reference("observation"));

        var schema = StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["note"] = observation.DeepClone(),
            ["nextAction"] = observation.DeepClone(),
            ["activePeriod"] = observation.DeepClone(),
            ["openItem"] = observation.DeepClone(),
            ["cases"] = observation,
        });

        schema["$defs"] = new JsonObject
        {
            ["observation"] = StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["text"] = StrictJsonSchema.StringType(),
                ["sources"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.PositionOf(sourceCount)),
            }),
        };

        return StrictJsonSchema.ResponseFormat(schema, Name, Description);
    }
}
