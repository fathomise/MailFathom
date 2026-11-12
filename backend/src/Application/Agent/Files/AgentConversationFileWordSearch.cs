// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Application.Agent.Files;

/// <summary>Ranks a conversation's passages by how many of a query's words each holds.</summary>
/// <remarks>
/// <para>
/// Read over the recorded words rather than through the database's text search: a conversation holds at most
/// <see cref="AgentConversationFileBounds.MaximumFilesPerConversation" /> documents, each bounded by what the reader takes
/// out of one, so the whole of it is one ordinary read, and the passages a hit names are the very ones the search by
/// meaning names.
/// </para>
/// <para>
/// A word matches wherever it appears, in any case, and a long word matches by its stem — all but its last two
/// letters — so an inflected form in the file is found by the form the question used. That is a deliberately rough
/// stem, and it is what lets the search by words hold up in a language that inflects; the search by meaning beside it
/// is what finds a passage that says the same thing in other words.
/// </para>
/// </remarks>
internal static partial class AgentConversationFileWordSearch
{
    /// <summary>The length from which a word is matched by its stem rather than whole.</summary>
    private const int StemmedFromLength = 6;

    /// <summary>How many closing letters a stem leaves off.</summary>
    private const int StemmedLetters = 2;

    /// <summary>Ranks every passage that holds at least one word of the query, most words first and reading order among equals.</summary>
    /// <param name="query">What to look for.</param>
    /// <param name="documents">Each document beside its passages.</param>
    /// <param name="limit">The most passages to return.</param>
    /// <returns>The passages, best first.</returns>
    internal static IReadOnlyList<AgentConversationFilePassage> Rank(
        string query,
        IEnumerable<(AgentConversationFileText Document, IReadOnlyList<AgentConversationFilePassage> Passages)> documents,
        int limit)
    {
        var terms = TermsOf(query);

        if (terms.Length is 0)
        {
            return [];
        }

        return
        [
            .. documents
                .SelectMany(static (pair, documentOrder) => pair.Passages.Select(passage => (pair.Document, Passage: passage, DocumentOrder: documentOrder)))
                .Select(candidate => (candidate.Passage, candidate.DocumentOrder, Score: ScoreOf(candidate.Document.Text, candidate.Passage, terms)))
                .Where(static scored => scored.Score > 0)
                .OrderByDescending(static scored => scored.Score)
                .ThenBy(static scored => scored.DocumentOrder)
                .ThenBy(static scored => scored.Passage.Ordinal)
                .Take(limit)
                .Select(static scored => scored.Passage),
        ];
    }

    /// <summary>Reads the distinct words of a query, each as the form it is matched by.</summary>
    internal static string[] TermsOf(string query) =>
    [
        .. Word().Matches(query)
            .Select(static match => match.Value)
            .Where(static word => word.Length >= 2)
            .Select(static word => word.ToUpperInvariant())
            .Select(static word => word.Length >= StemmedFromLength ? word[..^StemmedLetters] : word)
            .Distinct(StringComparer.Ordinal),
    ];

    private static int ScoreOf(string text, AgentConversationFilePassage passage, string[] terms)
    {
        var length = Math.Min(passage.EndOffset, text.Length) - passage.StartOffset;

        return terms.Count(term => text.AsSpan(passage.StartOffset, length).Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}
