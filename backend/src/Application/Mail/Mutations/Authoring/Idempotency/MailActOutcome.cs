// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Mutations.Authoring.Idempotency;

/// <summary>What the first attempt of an act answered for one message, kept so a repeat can answer it again.</summary>
/// <param name="Outcome">The name of the outcome member the recording use case answered with.</param>
/// <param name="Changes">The durable records the act opened for the message, which are the move's or the delete's own record where it has one.</param>
/// <param name="SpamMarkChanges">The records a move into the junk folder opened by marking the message spam, and empty for every other act.</param>
/// <param name="Account">The account the message belonged to, for a flag change, and <see langword="null" /> for every other act.</param>
/// <param name="Folder">The folder the message was in, for a flag change, and <see langword="null" /> for every other act.</param>
/// <param name="Destination">The alias of the folder a move was recorded against, and <see langword="null" /> for every other act and for a move that named none.</param>
/// <param name="IsMoved">Whether a spam mark also filed the message elsewhere.</param>
/// <remarks>
/// It is the application's own shape rather than any transport's, and it holds the record identities and the lifecycle
/// each had when the first attempt answered, not a copy of anything about the message: a replay after the message was
/// deleted still has its answer, which is the point of keeping it beside the key rather than reading the records.
/// </remarks>
public sealed record MailActOutcome(
    string Outcome,
    IReadOnlyList<RecordedMailFlagMutation> Changes,
    IReadOnlyList<RecordedMailFlagMutation> SpamMarkChanges,
    string? Account = null,
    string? Folder = null,
    string? Destination = null,
    bool IsMoved = false);
