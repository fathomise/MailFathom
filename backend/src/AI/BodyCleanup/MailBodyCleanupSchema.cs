// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.BodyCleanup;

/// <summary>The strict JSON schema one cleaning's answer is asked to be a document of, built for the outline it reads.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="MailBodyCleanupDocument" />: an array of ranges, each two block numbers and a verdict. A block
/// number is an <c>enum</c> of the outline's own numbers, falling back to a plain integer for an outline longer than
/// <see cref="StrictJsonSchema.MaximumEnumeratedNumbers" /> blocks, and the verdict is an <c>enum</c> of the two words the
/// reading accepts, so a range naming a block the message does not have, or a fate nobody stated, is an answer a provider
/// honouring the schema cannot produce.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so what it cannot state stays the partition check's,
/// and every one of those is a refusal because meaning would be lost rather than a cut: an empty array, a range whose
/// end falls before its start, a gap, an overlap, ranges out of order, and a number past the outline beyond the
/// enumerated bound each leave some block's fate unstated or stated twice, and a range is not a claim one can keep while
/// dropping its neighbours. The reader is shown the uncleaned message instead.
/// </para>
/// </remarks>
internal static class MailBodyCleanupSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "body_cleanup";

    private const string Description = "Which blocks of one email message a reader is shown, as ranges of block numbers that partition the outline.";

    /// <summary>Builds the response format for one outline.</summary>
    /// <param name="blockCount">How many blocks the outline holds, which a range names by number from zero.</param>
    /// <returns>The strict schema's response format.</returns>
    internal static ChatResponseFormatJson For(int blockCount)
    {
        var schema = StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["segments"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["from"] = StrictJsonSchema.PositionOf(blockCount),
                ["to"] = StrictJsonSchema.PositionOf(blockCount),
                ["action"] = StrictJsonSchema.Enumeration([MailBodyCleanupInstructions.KeepAction, MailBodyCleanupInstructions.DropAction]),
            })),
        });

        return StrictJsonSchema.ResponseFormat(schema, Name, Description);
    }
}
