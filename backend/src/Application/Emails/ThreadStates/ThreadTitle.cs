// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>What a conversation is called on a thread list: what it is about, and where it stands now.</summary>
/// <remarks>
/// <para>
/// Two parts because a row has to stay recognizable while it moves. The topic is set early and kept, so a reply never
/// turns the row somebody is looking for into a different one; the standing is written again with every derivation, so
/// the same row says what is still open now. <see cref="ThreadTitleProposal" /> is what keeps the topic, by rule rather
/// than by trusting a model to repeat itself.
/// </para>
/// <para>
/// It is part of the conversation's state rather than a record of its own: the same answer writes both, so it is
/// settled, withheld, re-derived, served, and erased with the statements beside it.
/// </para>
/// </remarks>
public sealed record ThreadTitle
{
    /// <summary>The greatest length a topic may have.</summary>
    /// <remarks>One line of a list row, which a longer value would end in an ellipsis long before it was read.</remarks>
    public const int MaximumTopicLength = 80;

    /// <summary>The greatest length a standing may have.</summary>
    /// <remarks>One line under the topic, long enough for two short clauses — what is owed and what is still open.</remarks>
    public const int MaximumStandingLength = 160;

    private static readonly string[] RefusedSequences = ["](", "__", "://", "www.", "mailto:"];

    private ThreadTitle(string topic, string standing)
    {
        this.Topic = topic;
        this.Standing = standing;
    }

    /// <summary>Gets what the conversation is about.</summary>
    public string Topic { get; init; }

    /// <summary>Gets where the conversation stands, which is empty where nothing in it is left open, owed, or awaited.</summary>
    public string Standing { get; init; }

    /// <summary>Records a title out of two values already known to be plain lines.</summary>
    /// <param name="topic">What the conversation is about.</param>
    /// <param name="standing">Where it stands, or empty where nothing is left open.</param>
    /// <returns>The title.</returns>
    /// <exception cref="ArgumentException">Thrown when the topic is not one plain line within its bound, or the standing is neither empty nor one.</exception>
    public static ThreadTitle Create(string topic, string standing)
    {
        if (!IsPlainLine(topic, MaximumTopicLength))
        {
            throw new ArgumentException("A topic is one plain line within its bound.", nameof(topic));
        }

        if (standing.Length > 0 && !IsPlainLine(standing, MaximumStandingLength))
        {
            throw new ArgumentException("A standing is empty or one plain line within its bound.", nameof(standing));
        }

        return new ThreadTitle(topic, standing);
    }

    /// <summary>Decides whether a value a model wrote may be drawn as one line of plain text.</summary>
    /// <param name="value">The value, as written.</param>
    /// <param name="maximumLength">The longest it may be.</param>
    /// <returns><see langword="true" /> where the value is non-blank, within the bound, on one line, and carries no markup and no link.</returns>
    /// <remarks>
    /// A refusal rather than a repair, because a value that fails it is a model not answering what it was asked, and a
    /// title is drawn where a sender's words are not: markup or a link in it would be a sender's text reaching a place
    /// the deployment writes in. The marks refused are the ones that open an HTML tag, Markdown emphasis or code, or a
    /// Markdown link, and the forms a link is written in. A redaction placeholder carries none of them.
    /// </remarks>
    public static bool IsPlainLine(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.Trim().Length == value.Length
        && !value.Any(static character => char.IsControl(character) || character is '<' or '>' or '`' or '*')
        && !RefusedSequences.Any(sequence => value.Contains(sequence, StringComparison.OrdinalIgnoreCase));
}
