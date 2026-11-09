// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.CalendarEvents;

/// <summary>The strict JSON schema an extraction's answer is asked to be a document of.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="CalendarEventExtractionDocument" />: an array of events, each a title, a start, and an end that
/// is <c>null</c> where the text states no length. Nothing in it depends on the turn — the text cites nothing by number —
/// so it is built once.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so no bound is stated here.
/// <see cref="CalendarEventExtractionReading" /> keeps the leading events of an array that runs past the reading's
/// count, and cuts a title that only runs past its length, because the occasion survives both. It drops an event whose
/// title is blank or carries a control character, and one whose start is not a local date and time in the form the
/// instruction names, because there is then no occasion to offer — an event with no name, or no day, is one nobody
/// could put on a calendar. An end that does not read, or is not after its start, is dropped alone and the event kept.
/// </para>
/// </remarks>
internal static class CalendarEventExtractionSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "calendar_events";

    private const string Description = "The calendar events a text names, each with its title, its local start, and its local end where the text states one.";

    /// <summary>Gets the response format every extraction sends where its model declares a strict schema.</summary>
    internal static ChatResponseFormatJson ResponseFormat { get; } = StrictJsonSchema.ResponseFormat(
        StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["events"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["title"] = StrictJsonSchema.StringType(),
                ["start"] = StrictJsonSchema.StringType(),
                ["end"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
            })),
        }),
        Name,
        Description);
}
