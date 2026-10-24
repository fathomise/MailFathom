// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>One change on a message, with everything that was settled about it before the transaction it commits in opened.</summary>
/// <param name="Request">The change asked for.</param>
/// <param name="Destination">Where a relocation or a copy files the message; <see langword="null" /> for every other change.</param>
/// <param name="HeldUntil">How long a written record waits before a convergence pass may take it in hand.</param>
/// <param name="PersonsMove">Whether a person asked for this move, which is the only requester refused a move into the folder the message is in or across the edge of the drafts or the sent tree.</param>
/// <param name="CrossesDraftsOrSentTreeOnTheServer">Whether the move would cross the edge of the drafts or the sent tree, judged by the folder the message was stored under on the mail server.</param>
/// <param name="PreparedCopy">The payload a local copy was placed with before the transaction; <see langword="null" /> for every other change, and for a copy whose payload could not be placed.</param>
public sealed record MailboxChange(
    MailboxMutationRequest Request,
    MailboxDestination? Destination,
    DateTimeOffset? HeldUntil,
    bool PersonsMove,
    bool CrossesDraftsOrSentTreeOnTheServer,
    PreparedLocalCopy? PreparedCopy);
