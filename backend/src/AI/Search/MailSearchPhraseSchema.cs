// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.Search.Phrasing;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Search;

/// <summary>The strict JSON schema a sentence's reading is asked to be a document of.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="MailSearchPhraseDocument" />: the filters, the criteria, and the part left unaccounted for. The
/// turn fixes no number, so the schema is the same for every sentence and is built once. Each filter that does not
/// apply is <c>null</c>, and each of the three switches is <c>false</c> where the sentence does not ask for it, which is
/// what the reading already makes of a switch that is not <c>true</c>; a filters object saying nothing is no filter, and
/// an answer saying nothing at all is the plain word search a deployment with no model runs.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so what it cannot state stays the reading's. Each is a
/// value dropped rather than the answer refused, because the reading never refuses one. Criteria past
/// <see cref="MailSearchPhraseReading.MaximumCriteria" /> are cut to the leading ones. A criterion or an unaccounted part
/// past its length is dropped rather than cut, because it is shown to the person as their own words and a phrase cut
/// mid-way reads as something they did not write. An address that could not be one, a day not written
/// <c>YYYY-MM-DD</c>, and a range ending before it starts are dropped, the range whole, since keeping its readable half
/// would narrow the search by something nobody said.
/// </para>
/// </remarks>
internal static class MailSearchPhraseSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "search_phrase";

    private const string Description = "What a search for the mail one sentence describes should be made of: filters, criteria, and the part made nothing of.";

    /// <summary>Gets the response format every reading asks for.</summary>
    internal static ChatResponseFormatJson ResponseFormat { get; } = StrictJsonSchema.ResponseFormat(
        StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["filters"] = StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["senderAddress"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["recipientAddress"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["receivedFrom"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["receivedTo"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
                ["unread"] = StrictJsonSchema.BooleanType(),
                ["flagged"] = StrictJsonSchema.BooleanType(),
                ["hasAttachments"] = StrictJsonSchema.BooleanType(),
            }),
            ["criteria"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.StringType()),
            ["unaccounted"] = StrictJsonSchema.Nullable(StrictJsonSchema.StringType()),
        }),
        Name,
        Description);
}
