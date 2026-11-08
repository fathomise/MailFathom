// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Planning;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Discovery;

/// <summary>The strict JSON schema a question's plan is asked to be a document of.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="DiscoveryPlanDocument" />: an intent from the catalogue <see cref="DiscoveryIntent" /> holds,
/// the lookups, and how many extracts would be enough. The turn's only number is the retrieval bound, which the reading
/// clamps to, so the schema is the same for every question and is built once. Each filter a lookup does not set is
/// <c>null</c> — a read, starred, or attachment switch included, because <c>false</c> there is a filter of its own: the
/// unread, the unstarred, or the mail carrying no file.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so what it cannot state stays the reading's, and
/// <see cref="DiscoveryPlanReading" /> refuses none of it. Lookups past <see cref="RetrievalPlan.MaximumLookups" />
/// are cut to the leading ones. A lookup whose words are blank or run past what can be searched for is dropped on its
/// own, since cutting it would search for words the model did not choose. A received bound not written in the anchored
/// form is dropped, which widens that lookup rather than narrowing it onto hours nobody named. An enough outside one and
/// the retrieval bound is clamped. And a plan whose lookups all fall away — an empty array included — keeps its intent
/// and its enough and searches the question's own words, the lookup a deployment with no model runs.
/// </para>
/// </remarks>
internal static class DiscoveryPlanningSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "discovery_plan";

    private const string Description = "How one question about a mailbox should be searched for: its kind, the lookups to run, and how many extracts would answer it.";

    /// <summary>Gets the response format every plan asks for.</summary>
    internal static ChatResponseFormatJson ResponseFormat { get; } = StrictJsonSchema.ResponseFormat(
        StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["intent"] = StrictJsonSchema.Enumeration(DiscoveryIntent.All.Select(static intent => intent.Identity)),
            ["lookups"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["queryText"] = StrictJsonSchema.StringType(),
                ["senderAddress"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["recipientAddress"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["subjectFragment"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["receivedOnOrAfter"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["receivedBefore"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["isRemotelySeen"] = StrictJsonSchema.Nullable(StrictJsonSchema.BooleanType()),
                ["isRemotelyFlagged"] = StrictJsonSchema.Nullable(StrictJsonSchema.BooleanType()),
                ["keyword"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["hasAttachments"] = StrictJsonSchema.Nullable(StrictJsonSchema.BooleanType()),
            })),
            ["sufficientPassages"] = StrictJsonSchema.IntegerType(),
        }),
        Name,
        Description);
}
