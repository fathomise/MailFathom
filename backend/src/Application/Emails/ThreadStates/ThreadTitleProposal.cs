// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>The title one derivation proposed, before the rule that keeps a conversation's topic has been applied to it.</summary>
/// <param name="Topic">The new topic the derivation wrote, or <see langword="null" /> where it answered that the topic it was shown stays.</param>
/// <param name="StatesSubjectChange">Whether the derivation stated that the conversation moved on to another subject.</param>
/// <param name="Standing">Where the conversation stands now, or empty where nothing in it is left open.</param>
/// <remarks>
/// A model is shown the topic it gave last time and asked to keep it, so a reply that adds a detail does not rename the
/// row. That is a request rather than a guarantee, which is why the decision is taken here: <see cref="SettleFor" />
/// accepts a new topic freely only while the conversation is young, and after that only where the derivation said the
/// subject changed. The standing is taken every time, because it exists to move.
/// </remarks>
public sealed record ThreadTitleProposal(string? Topic, bool StatesSubjectChange, string Standing)
{
    /// <summary>The most messages a conversation may hold and still take a new topic without stating a subject change.</summary>
    /// <remarks>
    /// The first exchanges are where a conversation finds out what it is about — an opening question and the first two
    /// answers — so a topic written from the first message alone is allowed to be corrected. Past that, the row has been
    /// seen under its name, and renaming it would turn the row somebody is looking for into another one.
    /// </remarks>
    public const int YoungConversationMessageCount = 3;

    /// <summary>Settles the proposal against the conversation it was derived from.</summary>
    /// <param name="thread">The conversation, carrying the topic its stored state kept and the shape it was read at.</param>
    /// <returns>
    /// The title, or <see langword="null" /> where the conversation is given none: every message the derivation was
    /// shown sits in junk, or nothing proposes a topic and nothing was kept.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="thread" /> is <see langword="null" />.</exception>
    /// <remarks>A conversation that is all junk is a spam conversation, and a name on its row would be the deployment vouching for it.</remarks>
    public ThreadTitle? SettleFor(DerivableThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (thread.Messages.All(static message => message.Provenance.IsInJunkFolder))
        {
            return null;
        }

        var acceptsNewTopic = thread.KeptTopic is null
            || this.StatesSubjectChange
            || thread.Revision.MessageCount <= YoungConversationMessageCount;

        return (acceptsNewTopic ? this.Topic ?? thread.KeptTopic : thread.KeptTopic) is { } topic
            ? ThreadTitle.Create(topic, this.Standing)
            : null;
    }
}
