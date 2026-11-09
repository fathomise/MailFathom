// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Emails;

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
    /// <summary>Reads an agent's answer into a derivation.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="messages">The messages the turn published, in the order it numbered them.</param>
    /// <param name="topicShown">Whether the turn carried the topic the conversation was given before, which is the only topic an answer may keep.</param>
    /// <returns>
    /// A settled derivation of the statements that survived, in aspect order and empty where none did, beside the title
    /// the answer proposed — or one withheld as <see cref="ThreadStateWithholding.AnswerUnreadable" /> where the answer
    /// carries no object this build reads, or a title line that is not plain text.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    internal static ThreadStateDerivation Read(
        string? answerText,
        IReadOnlyList<DerivableThreadMessage> messages,
        bool topicShown)
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

        if (document.Title is not { } writtenTitle || ProposesNothing(writtenTitle))
        {
            return ThreadStateDerivation.Settled(entries);
        }

        return ReadTitle(writtenTitle, entries, topicShown) is { } title
            ? ThreadStateDerivation.Settled(entries, title)
            : ThreadStateDerivation.Withholding(ThreadStateWithholding.AnswerUnreadable);
    }

    /// <summary>Reads the title the model wrote, or nothing where either of its lines is not one this build draws.</summary>
    /// <remarks>
    /// <para>
    /// A line that only runs long is repaired, as a statement that runs long is: it is cut at its bound and marked as
    /// cut, because a topic two characters past the limit is still the line the row is recognized by, and withholding
    /// the whole answer for it threw away every statement beside it. Anything else is a refusal. A topic with markup, a
    /// link, or a second line in it is a model not answering what it was asked, and the answer is withheld exactly as
    /// one that does not parse is. An answer carrying no title at all is the other case and settles proposing none,
    /// which leaves the conversation the topic it already had.
    /// </para>
    /// <para>
    /// Keeping the topic is an answer only where the turn showed one. Written where it showed none, it keeps nothing and
    /// would leave the conversation untitled, so it is read as no answer about the topic at all and the title stands or
    /// falls on the topic written beside it. Where the turn did show one, a title naming no new topic keeps it whether or
    /// not it says so: the one shown is the topic until the conversation moves on, and a model writing only a standing
    /// has proposed nothing that would replace it.
    /// </para>
    /// <para>
    /// The standing is kept only beside a statement it can be reading: an open question or a commitment. A standing
    /// written beside nothing still open is a line stating something no statement carries, so it is stored empty —
    /// which is also what a conversation with nothing left open is owed.
    /// </para>
    /// </remarks>
    private static ThreadTitleProposal? ReadTitle(
        ThreadTitleDocument written,
        IReadOnlyList<ThreadStateEntry> entries,
        bool topicShown)
    {
        var standing = written.Standing?.Trim() ?? string.Empty;

        if (standing.Length > 0 && !IsPlainWhole(standing))
        {
            return null;
        }

        standing = Shortened(standing, ThreadTitle.MaximumStandingLength);

        if (!entries.Any(static entry => entry.Aspect is ThreadStateAspect.OpenQuestion or ThreadStateAspect.Commitment))
        {
            standing = string.Empty;
        }

        var topic = written.Topic?.Trim();

        if (topicShown && (written.KeepTopic is true || string.IsNullOrEmpty(topic)))
        {
            return new ThreadTitleProposal(Topic: null, StatesSubjectChange: false, standing);
        }

        return topic is not null && IsPlainWhole(topic)
            ? new ThreadTitleProposal(
                Shortened(topic, ThreadTitle.MaximumTopicLength),
                written.SubjectChanged is true,
                standing)
            : null;
    }

    /// <summary>Whether a title says nothing at all, every member left out or written as <c>null</c> or blank.</summary>
    /// <remarks>
    /// Read as no title rather than as a title naming no topic, because it is the empty answer a strict schema's required
    /// members stand for: an answer that left the title out settles proposing none, and so does one that wrote it empty.
    /// </remarks>
    private static bool ProposesNothing(ThreadTitleDocument written) =>
        string.IsNullOrWhiteSpace(written.Topic) && written.KeepTopic is not true && string.IsNullOrWhiteSpace(written.Standing);

    /// <summary>Judges a whole title line as written, before any cut, so markup or a break past the bound is still refused.</summary>
    private static bool IsPlainWhole(string line) => ThreadTitle.IsPlainLine(line, line.Length);

    /// <summary>Cuts a title line past its bound and marks it as cut, on a text-element boundary.</summary>
    private static string Shortened(string line, int maximumLength) =>
        line.Length <= maximumLength
            ? line
            : MailTextBounds.TruncateAtTextElementBoundary(line, maximumLength - 1).TrimEnd() + "…";

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

    private static ThreadStateDocument? ReadDocument(string? answerText) =>
        AgentJsonAnswer.Read(answerText, ThreadStateJsonContext.Default.ThreadStateDocument);
}
