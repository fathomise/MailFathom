// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>Everything one drafting is written from: the correspondence it answers, who is in it, and how its author writes.</summary>
/// <param name="Subject">The subject the conversation is read under, or <see langword="null" /> where no message carried one.</param>
/// <param name="Messages">The most recent messages of the conversation, or of every conversation the context reaches, in the order they were written, which is what the draft is grounded in and cites.</param>
/// <param name="Participants">The people those messages name, which is the whole set a proposed recipient may come from.</param>
/// <param name="StyleSamples">What the person themselves has recently sent, which the draft's manner is derived from, and which is empty where a deployment derives no style.</param>
/// <remarks>
/// <para>
/// Deliberately narrow, exactly as a conversation's own derivation is. What a draft is written from is what the people
/// in the exchange said and how this person writes; no folder, no account, and no mailbox is part of either, and every
/// one of them would be somebody's data sent to a provider for nothing the reply would gain.
/// </para>
/// <para>
/// The participants carry an address and the messages do not, and the difference is load-bearing: an address never
/// travels to a provider, and a proposed recipient is answered as a position in this list rather than as text a model
/// wrote. That is what makes it impossible for a drafting to address a reply to somebody the conversation never named.
/// </para>
/// </remarks>
public sealed record ReplyDraftSources(
    string? Subject,
    IReadOnlyList<ReplyDraftMessage> Messages,
    IReadOnlyList<ReplyDraftParticipant> Participants,
    IReadOnlyList<string> StyleSamples)
{
    /// <summary>What a message answering no correspondence is written from, which is nothing this deployment holds.</summary>
    /// <remarks>
    /// Not a degenerate reply but the honest description of that case: there is no exchange, so there is nobody to
    /// propose, nothing to cite, and no manner to take from an account the request never named. What the draft has
    /// instead is the instruction its author typed, which is why the use case requires one there.
    /// </remarks>
    public static ReplyDraftSources Nothing { get; } = new(Subject: null, [], [], []);

    /// <summary>Puts the reads of several context messages together into what one drafting is written from.</summary>
    /// <param name="reads">One read per context message, the one deciding the subject and the manner first.</param>
    /// <param name="maximumMessages">How many messages the merged correspondence carries at most.</param>
    /// <returns>The merged sources, numbered afresh so every position is the place it holds in the merged lists.</returns>
    /// <remarks>
    /// <para>
    /// Two context messages of one conversation read the same conversation, so a message is kept once, by its identity,
    /// and the people once, by their normalized address. The messages are ordered by when they were written, which is
    /// what lets a reply drawing on several exchanges answer where each of them got to; a message carrying no date is
    /// placed before every dated one, in read order, so it is the first the bound drops — what is kept is the most
    /// recent end of the whole.
    /// </para>
    /// <para>
    /// The subject and the style samples are the first read's alone. A reply is written in one account's manner, and the
    /// caller reads the other context messages with no sample bound for exactly that reason; taking a sample from any of
    /// them would be the second voice this shape rules out.
    /// </para>
    /// <para>
    /// The first read leaves only its own conversation out of the samples, so a message the person sent in another
    /// context conversation can arrive twice: once as a sample and once as a numbered message. Both are cut from the
    /// start of the same body text, so one is always a prefix of the other, and a sample that is the opening of a merged
    /// message is dropped. Without that the request would carry the text twice and make it the loudest example of how
    /// the person writes.
    /// </para>
    /// <para>
    /// One read is returned as it is, which is what keeps a reply to one message exactly what it was before a context
    /// could hold several.
    /// </para>
    /// </remarks>
    internal static ReplyDraftSources Merged(IReadOnlyList<ReplyDraftSources> reads, int maximumMessages)
    {
        if (reads is [var only])
        {
            return only;
        }

        var messages = reads
            .SelectMany(static read => read.Messages)
            .DistinctBy(static message => message.StoredEmailId)
            .OrderBy(static message => message.SentAt)
            .TakeLast(maximumMessages)
            .ToArray();

        return new ReplyDraftSources(
            reads.Count > 0 ? reads[0].Subject : null,
            [.. messages.Select(static (message, position) => message with { Position = position })],
            [
                .. reads
                    .SelectMany(static read => read.Participants)
                    .DistinctBy(static participant => participant.Address.NormalizedAddress, StringComparer.Ordinal)
                    .Select(static (participant, position) => participant with { Position = position }),
            ],
            reads.Count > 0 ? [.. reads[0].StyleSamples.Where(sample => !messages.Any(message => SameOpening(sample, message.Text)))] : []);
    }

    /// <summary>Whether a style sample and a message's text were cut from the start of the same body.</summary>
    private static bool SameOpening(string sample, string text) =>
        sample.Length > 0
        && text.Length > 0
        && (sample.StartsWith(text, StringComparison.Ordinal) || text.StartsWith(sample, StringComparison.Ordinal));
}

/// <summary>One message of the correspondence as a drafting is shown it.</summary>
/// <param name="StoredEmailId">The message, which is what a statement drawn from it cites.</param>
/// <param name="Position">The zero-based place the message holds in the order the turn publishes, which is how a citation names it.</param>
/// <param name="AuthorDisplayName">The name the message was written under, or <see langword="null" /> where it carried none.</param>
/// <param name="SentAt">When the message was written, which is what a date its text states relatively is read against.</param>
/// <param name="Text">What the message added, with the history it quoted already trimmed off, bounded by what one turn may carry.</param>
public sealed record ReplyDraftMessage(
    StoredEmailId StoredEmailId,
    int Position,
    string? AuthorDisplayName,
    DateTimeOffset? SentAt,
    string Text);

/// <summary>One person the conversation names, as a proposed recipient resolves back to.</summary>
/// <param name="Position">The zero-based place the person holds in the list the turn publishes, which is how a proposal names them.</param>
/// <param name="Address">Their address, which stays inside this deployment and is never put to a provider.</param>
public sealed record ReplyDraftParticipant(int Position, EmailAddress Address);
