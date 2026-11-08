// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.Application.EmailContent.Cleaning;

namespace MailFathom.AI.BodyCleanup;

/// <summary>Turns what a body-cleanup agent wrote into the ranges a cleaning is proposed as.</summary>
/// <remarks>
/// <para>
/// Every reading here is a pure function of the answer text, which is what makes the cases a provider produces once in a
/// thousand runs ordinary examples in a test rather than something only a live endpoint reaches.
/// </para>
/// <para>
/// <b>It refuses rather than repairing, and refuses whole rather than per range.</b> The ranges are a partition of one
/// document, so a range this could not read is not one claim among several — it is the statement about those blocks
/// missing, which makes every range after it describe blocks nobody spoke for. An empty list is what it answers with, and
/// the caller reads that as an answer it cannot use: one further attempt, and then the uncleaned message.
/// </para>
/// </remarks>
internal static class MailBodyCleanupReading
{
    /// <summary>Reads the proposed ranges out of an agent's answer.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <returns>The ranges in the order they were written, which is empty where the answer was not a partition of block numbers.</returns>
    internal static IReadOnlyList<MailBodyCleaningSegment> Read(string? answerText)
    {
        if (ReadDocument(answerText) is not { Segments: { } written })
        {
            return [];
        }

        var segments = new List<MailBodyCleaningSegment>(written.Count);

        foreach (var segment in written)
        {
            if (segment is not { From: { } from, To: { } to } || ReadAction(segment.Action) is not { } keep)
            {
                return [];
            }

            segments.Add(new MailBodyCleaningSegment(from, to, keep));
        }

        return segments;
    }

    /// <summary>Reads the one keyword a range carries, or nothing where it carries something else.</summary>
    /// <remarks>
    /// Two words and no third. A range whose action is a sentence, a synonym, or absent is one whose fate for those
    /// blocks nobody stated, and guessing at it is the repair this reading refuses.
    /// </remarks>
    private static bool? ReadAction(string? action) => action?.Trim() switch
    {
        var stated when string.Equals(stated, MailBodyCleanupInstructions.KeepAction, StringComparison.OrdinalIgnoreCase)
            => true,
        var stated when string.Equals(stated, MailBodyCleanupInstructions.DropAction, StringComparison.OrdinalIgnoreCase)
            => false,
        _ => null,
    };

    private static MailBodyCleanupDocument? ReadDocument(string? answerText) =>
        AgentJsonAnswer.Read(answerText, MailBodyCleanupJsonContext.Default.MailBodyCleanupDocument);
}
