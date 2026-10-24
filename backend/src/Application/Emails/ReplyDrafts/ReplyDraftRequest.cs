// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>States which messages a reply is being drafted from, and what its author asked for.</summary>
/// <remarks>
/// <para>
/// Each message is named by its stable local identity and by nothing else, exactly as an answer somebody writes by hand
/// names the one it answers: the correspondence the draft is grounded in, the account whose sent mail its style is
/// derived from, and the people it may propose are all read out of the stored copies those identities resolve to, so a
/// caller can state none of them and can state none of them wrongly.
/// </para>
/// <para>
/// The context is the set of messages the person chose to write from — usually the one they are answering, and as many
/// as <see cref="MaximumContextMessages" />, from one conversation or from several. Each brings the recent end of its
/// own conversation with it, and the first one named decides the subject and the manner, because a reply is written
/// in one account's voice however many exchanges it draws on.
/// </para>
/// <para>
/// The two texts are what makes this a drafting request rather than a reading. A selection is the part of the
/// correspondence somebody pointed at — the paragraph they are actually answering — and an instruction is what they
/// want said. Both are optional, and a request carrying neither is the ordinary one: reply to this conversation.
/// </para>
/// <para>
/// <b>A request naming no message is the composer with nothing behind it</b>, which the same drafting answers because
/// it is the same act: somebody asking for text they will read, edit, and decide about. What changes is that there is
/// no correspondence to ground it in, so nothing is read, nothing is cited, nobody is proposed, and the instruction is
/// the whole of the request — which is why it is required there and optional beside a message.
/// </para>
/// </remarks>
public sealed record ReplyDraftRequest
{
    /// <summary>The greatest number of messages one drafting may be written from.</summary>
    /// <remarks>
    /// Each one is a read of the store and a share of the messages one turn carries, so past a handful the context stops
    /// being what the person chose and becomes a sample of their mailbox, while every share of the turn's bound grows
    /// too thin to carry the exchange it came from.
    /// </remarks>
    public const int MaximumContextMessages = 10;

    /// <summary>The greatest length an instruction may have.</summary>
    /// <remarks>A sentence or two saying what to say. Somebody writing more than this is writing the reply, which is the work they asked to be spared.</remarks>
    public const int MaximumInstructionLength = 1_000;

    /// <summary>The greatest length a selection may have.</summary>
    /// <remarks>The bound one message of the conversation already travels under, because a selection is part of one and a longer one would be the exchange sent twice.</remarks>
    public const int MaximumSelectionLength = 4_000;

    /// <summary>Gets the stored messages the reply is written from, first the one deciding its subject and manner, which is empty where the message being written answers none.</summary>
    public IReadOnlyList<StoredEmailId> ContextEmailIds { get; init; } = [];

    /// <summary>Gets the part of the correspondence the draft is to answer, or <see langword="null" /> to answer the conversation as a whole.</summary>
    public string? Selection { get; init; }

    /// <summary>Gets what the person asked the reply to say, or <see langword="null" /> where they asked for nothing in particular.</summary>
    public string? Instruction { get; init; }
}
