// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MailFathom.AI.Decisions;

/// <summary>What can be read off a set of closed questions before any of them is asked: the revision they are asked under, and how much of a request they take.</summary>
internal static class DecisionQuestionSet
{
    private const int RevisionLength = 12;

    /// <summary>What a request body carries around its questions and its input: the model, the routing block, and the member names.</summary>
    private const int RequestStructureCharacters = 512;

    /// <summary>What a request body adds around each question or option beyond its own text: keys, quotes, and separators.</summary>
    private const int ItemStructureCharacters = 48;

    /// <summary>Digests everything a set of questions says, so rewording any of it is a new revision.</summary>
    /// <param name="questions">The questions, in the order they are asked.</param>
    /// <returns>Twelve lowercase hexadecimal characters.</returns>
    internal static string RevisionOf(IReadOnlyList<DecisionQuestion> questions)
    {
        var canonical = new StringBuilder();

        foreach (var question in questions)
        {
            canonical.Append(question.Id).Append('\u001f').Append((int)question.Kind).Append('\u001f').Append(question.Instructions);

            foreach (var option in question.Options)
            {
                canonical.Append('\u001e').Append(option.Value).Append('\u001f').Append(option.Description);
            }

            canonical.Append('\u001d');
        }

        return DigestOf(canonical.ToString());
    }

    /// <summary>Digests one piece of text into the form a revision takes.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Twelve lowercase hexadecimal characters.</returns>
    internal static string DigestOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..RevisionLength];

    /// <summary>Gets how much of a request body a set of questions takes as the request carries them, so the input is cut to what is left.</summary>
    /// <param name="questions">The questions.</param>
    /// <returns>The characters to leave for the questions and the structure around them.</returns>
    /// <remarks>
    /// The questions' own text is measured as JSON escapes it, which is how the body the request bound is checked on carries
    /// it; the structure around it is a margin rather than a measurement, because the body's shape is the adapter's. A
    /// margin that fell short leaves the request refused as input too large, which is the outcome it had without one.
    /// </remarks>
    internal static int ReservedCharactersOf(IReadOnlyList<DecisionQuestion> questions) =>
        RequestStructureCharacters
        + questions.Sum(static question =>
            ItemStructureCharacters
            + EscapedLength(question.Id)
            + EscapedLength(question.Instructions)
            + question.Options.Sum(static option =>
                ItemStructureCharacters + EscapedLength(option.Value) + EscapedLength(option.Description)));

    private static int EscapedLength(string text) => JsonEncodedText.Encode(text).Value.Length;
}
