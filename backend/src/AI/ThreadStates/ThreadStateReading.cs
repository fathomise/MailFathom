// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Text.Json;
using MailFathom.Application.Emails.ThreadStates;

namespace MailFathom.AI.ThreadStates;

/// <summary>Turns what a thread-state agent wrote into statements, keeping only what the conversation itself can back.</summary>
/// <remarks>
/// <para>
/// Every reading below is a pure function of the answer and the messages the turn published, which is what makes the
/// cases a provider produces once in a thousand runs ordinary examples in a test rather than something only a live
/// endpoint reaches.
/// </para>
/// <para>
/// It drops rather than repairs. A statement with no text or no message this turn published is one nothing can be
/// checked against, and there is no honest way to invent the missing half — so it falls away and the conversation keeps
/// the statements that survived. An answer whose object was read and none of whose statements survived is a settled
/// state of no statements, because the model read the conversation and said nothing usable about it.
/// </para>
/// <para>
/// An answer with no object to read at all — prose, a reply cut off before its object closed, or text that does not
/// parse as the object the agent was asked for — is withheld instead, as
/// <see cref="ThreadStateWithholding.AnswerUnreadable" />. That says how one reply came back rather than anything about
/// the conversation, and settling it would record the conversation as having nothing to say until it next changed.
/// </para>
/// <para>
/// A citation is a position in the list the turn composed, so a number outside that list names no message and takes its
/// statement with it. That is the whole of why the model is shown numbers rather than identifiers: an out-of-range
/// number is unusable, while an identifier a model wrote would be a message of some other conversation and would look
/// valid.
/// </para>
/// </remarks>
internal static class ThreadStateReading
{
    private const string JsonFence = "```";

    /// <summary>Reads an agent's answer into a derivation.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="messages">The messages the turn published, in the order it numbered them.</param>
    /// <returns>
    /// A settled derivation of the statements that survived, in aspect order and empty where none did, beside the title
    /// the answer proposed — or one withheld as <see cref="ThreadStateWithholding.AnswerUnreadable" /> where the answer
    /// carries no object this build reads, or a title it does not draw.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    internal static ThreadStateDerivation Read(
        string? answerText,
        IReadOnlyList<DerivableThreadMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (ReadDocument(answerText) is not { } document)
        {
            return ThreadStateDerivation.Withholding(ThreadStateWithholding.AnswerUnreadable);
        }

        IReadOnlyList<ThreadStateEntry> entries =
        [
            .. Aspect(ThreadStateAspect.Agreement, document.Agreements, messages),
            .. Aspect(ThreadStateAspect.OpenQuestion, document.OpenQuestions, messages),
            .. Aspect(ThreadStateAspect.Commitment, document.Commitments, messages),
            .. Aspect(ThreadStateAspect.VersionDifference, document.Differences, messages),
        ];

        if (document.Title is not { } writtenTitle)
        {
            return ThreadStateDerivation.Settled(entries);
        }

        return ReadTitle(writtenTitle, entries) is { } title
            ? ThreadStateDerivation.Settled(entries, title)
            : ThreadStateDerivation.Withholding(ThreadStateWithholding.AnswerUnreadable);
    }

    /// <summary>Reads the title the model wrote, or nothing where either of its lines is not one this build draws.</summary>
    /// <remarks>
    /// <para>
    /// A refusal rather than a repair, unlike a statement that runs long. A title is the line a row is recognized by,
    /// so a topic with markup, a link, or a second line in it is a model not answering what it was asked, and the
    /// answer is withheld exactly as one that does not parse is. An answer carrying no title at all is the other case
    /// and settles without one, as an answer of no statements settles without any.
    /// </para>
    /// <para>
    /// The standing is kept only beside a statement it can be reading: an open question or a commitment. A standing
    /// written beside nothing still open is a line stating something no statement carries, so it is stored empty —
    /// which is also what a conversation with nothing left open is owed.
    /// </para>
    /// </remarks>
    private static ThreadTitleProposal? ReadTitle(ThreadTitleDocument written, IReadOnlyList<ThreadStateEntry> entries)
    {
        var standing = written.Standing?.Trim() ?? string.Empty;

        if (standing.Length > 0 && !ThreadTitle.IsPlainLine(standing, ThreadTitle.MaximumStandingLength))
        {
            return null;
        }

        if (!entries.Any(static entry => entry.Aspect is ThreadStateAspect.OpenQuestion or ThreadStateAspect.Commitment))
        {
            standing = string.Empty;
        }

        if (written.KeepTopic is true)
        {
            return new ThreadTitleProposal(Topic: null, StatesSubjectChange: false, standing);
        }

        var topic = written.Topic?.Trim();

        return ThreadTitle.IsPlainLine(topic, ThreadTitle.MaximumTopicLength)
            ? new ThreadTitleProposal(topic, written.SubjectChanged is true, standing)
            : null;
    }

    /// <summary>Reads one array of written statements into the statements of one aspect.</summary>
    /// <remarks>
    /// A model handed a long exchange will happily write one agreement per message, so the leading entries that
    /// survive are kept and the rest fall away: a producer told to write the most important first does.
    /// </remarks>
    private static IEnumerable<ThreadStateEntry> Aspect(
        ThreadStateAspect aspect,
        IReadOnlyList<ThreadStateEntryDocument?>? written,
        IReadOnlyList<DerivableThreadMessage> messages) =>
        (written ?? [])
            .Select(entry => ToEntry(aspect, entry, messages))
            .OfType<ThreadStateEntry>()
            .Take(EmailThreadState.MaximumEntriesPerAspect);

    /// <summary>Turns one written statement into a statement, or into nothing where the conversation cannot back it.</summary>
    private static ThreadStateEntry? ToEntry(
        ThreadStateAspect aspect,
        ThreadStateEntryDocument? written,
        IReadOnlyList<DerivableThreadMessage> messages)
    {
        if (written is null || string.IsNullOrWhiteSpace(written.Text))
        {
            return null;
        }

        var sources = (written.Messages ?? [])
            .Where(position => position >= 0 && position < messages.Count)
            .Distinct()
            .Select(position => messages[position].StoredEmailId)
            .Take(ThreadStateEntry.MaximumSourceCount)
            .ToArray();

        if (sources.Length is 0)
        {
            return null;
        }

        var isCommitment = aspect is ThreadStateAspect.Commitment;

        return ThreadStateEntry.Create(
            aspect,
            written.Text,
            sources,
            isCommitment ? written.OwedBy : null,
            isCommitment ? ReadDueAt(written.DueAt) : null);
    }

    /// <summary>Reads the instant a commitment falls due, or nothing where the model wrote something that is not one.</summary>
    /// <remarks>
    /// The date falls away on its own rather than taking the commitment with it. A commitment somebody made is still
    /// worth showing when the model wrote its date in a shape nothing can parse, and a date is the part of a statement
    /// a reader would otherwise act on without checking.
    /// </remarks>
    private static DateTimeOffset? ReadDueAt(string? dueAt) =>
        DateTimeOffset.TryParse(
            dueAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static ThreadStateDocument? ReadDocument(string? answerText)
    {
        if (Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, ThreadStateJsonContext.Default.ThreadStateDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Finds the JSON object inside whatever the model wrote around it.</summary>
    /// <remarks>
    /// A model told to answer with one object still fences it, prefaces it, or writes a sentence after it often enough
    /// that treating any of those as a failed derivation would throw away usable statements. The outermost braces are
    /// what is read; anything either side of them is discarded unexamined.
    /// </remarks>
    private static string? Unfenced(string? answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText))
        {
            return null;
        }

        var text = answerText.Replace(JsonFence, string.Empty, StringComparison.Ordinal);
        var opening = text.IndexOf('{', StringComparison.Ordinal);
        var closing = text.LastIndexOf('}');

        return opening >= 0 && closing > opening ? text[opening..(closing + 1)] : null;
    }
}
