// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.ReplyDrafts;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>Turns what a passage-rewriting agent wrote into a rewriting, or into nothing where it does not answer every passage.</summary>
/// <remarks>
/// <para>
/// A pure function of the answer and the number of passages the turn asked about, so the shapes a provider produces once
/// in a thousand runs are ordinary examples in a test.
/// </para>
/// <para>
/// An answer is all or nothing. One text too few or too many leaves no way to tell which passage each text belongs to,
/// and a blank one would read as a passage the person asked to have deleted, so either is <see cref="PassageRewrite.Nothing" />
/// rather than a guess. Each text is trimmed and held to <see cref="PassageRewrite.MaximumPassageLength" /> by the
/// rewriting itself, and a line break inside one is read as a space, because a passage stands inside one paragraph and a
/// break written into it would split that paragraph in the draft.
/// </para>
/// </remarks>
internal static class PassageRewriteReading
{
    /// <summary>Reads the rewriting out of an agent's answer.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="passageCount">How many passages the turn asked about, which is how many texts the answer must carry.</param>
    /// <returns>The rewriting, or <see cref="PassageRewrite.Nothing" /> where the answer does not carry one text for every passage.</returns>
    internal static PassageRewrite Read(string? answerText, int passageCount)
    {
        if (AgentJsonAnswer.Read(answerText, ReplyDraftJsonContext.Default.PassageRewriteDocument) is not { Passages: { } passages }
            || passages.Count != passageCount
            || passages.Any(string.IsNullOrWhiteSpace))
        {
            return PassageRewrite.Nothing;
        }

        return PassageRewrite.Written([.. passages.Select(static passage => passage!.ReplaceLineEndings(" "))]);
    }
}
