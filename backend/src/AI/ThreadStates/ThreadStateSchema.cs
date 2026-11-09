// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.ThreadStates;

/// <summary>The strict JSON schema one derivation's answer is asked to be a document of, built for the conversation it reads.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="ThreadStateDocument" />: four arrays of statements, each citing messages by the positions the
/// turn gave them, and a title. A cited position is an <c>enum</c> of the conversation's own positions, so a statement
/// resting on a message the conversation does not hold is an answer a provider honouring the schema cannot produce.
/// </para>
/// <para>
/// The title is shaped by whether the turn showed a topic. Where it showed none, <c>topic</c> is a required string and
/// there is no <c>keepTopic</c>, because there is nothing to keep and a title naming no topic would leave the conversation
/// untitled. Where it showed one, <c>topic</c> is nullable beside <c>keepTopic</c>, and a title naming no new topic
/// keeps the one shown — which is how <see cref="ThreadStateReading" /> reads it whether or not <c>keepTopic</c> says so.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so no bound is stated here: the reading cuts a line
/// or a statement that runs long, keeps the leading entries of an array that runs long, and drops a date it cannot
/// parse. What it still refuses is a title line holding markup, a link, or a line break, and a topic of no words where
/// there is none to keep — a line a list of conversations would draw as somebody's mail rather than as a name, and a
/// conversation left nameless, neither of which has an honest repair.
/// </para>
/// </remarks>
internal static class ThreadStateSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "thread_state";

    private const string Description = "Where one email conversation stands: its agreements, open questions, commitments, version differences, and title.";

    /// <summary>Builds the response format for one conversation.</summary>
    /// <param name="messageCount">How many messages the conversation holds, which a statement cites by position.</param>
    /// <param name="topicShown">Whether the turn carries the topic the conversation was given before.</param>
    /// <returns>The strict schema's response format.</returns>
    internal static ChatResponseFormatJson For(int messageCount, bool topicShown)
    {
        var schema = StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["agreements"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.Reference("statement")),
            ["openQuestions"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.Reference("statement")),
            ["commitments"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.Reference("commitment")),
            ["differences"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.Reference("statement")),
            ["title"] = Title(topicShown),
        });

        var messages = StrictJsonSchema.ArrayOf(StrictJsonSchema.PositionOf(messageCount));

        schema["$defs"] = new JsonObject
        {
            ["statement"] = StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["text"] = StrictJsonSchema.StringType(),
                ["messages"] = messages.DeepClone(),
            }),
            ["commitment"] = StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["text"] = StrictJsonSchema.StringType(),
                ["messages"] = messages,
                ["owedBy"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["dueAt"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
            }),
        };

        return StrictJsonSchema.ResponseFormat(schema, Name, Description);
    }

    private static JsonObject Title(bool topicShown) => topicShown
        ? StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["topic"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
            ["keepTopic"] = StrictJsonSchema.BooleanType(),
            ["subjectChanged"] = StrictJsonSchema.BooleanType(),
            ["standing"] = StrictJsonSchema.StringType(),
        })
        : StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["topic"] = StrictJsonSchema.StringType(),
            ["subjectChanged"] = StrictJsonSchema.BooleanType(),
            ["standing"] = StrictJsonSchema.StringType(),
        });
}
