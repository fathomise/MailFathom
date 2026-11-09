// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Domain.Mutations;

/// <summary>What a change committed locally on a mirrored account last knew of the server, for the fields it wrote.</summary>
/// <param name="ObservedSeenState">The server's <c>\Seen</c> as last observed, for a change that wrote it.</param>
/// <param name="ObservedFlaggedState">The server's <c>\Flagged</c> as last observed, for a change that wrote it.</param>
/// <param name="ObservedKeywords">The server's whole keyword set as last observed, for a change that wrote any keyword.</param>
/// <param name="ObservedFolder">The folder the message was in before a move wrote another one.</param>
/// <param name="LocalCopy">The second stored message a copy wrote locally, which the copy's placement binds.</param>
/// <remarks>
/// <para>
/// A record carrying one overlays the stored message: while it has not ended, the act is the only writer of the fields
/// it wrote and synchronization records what it observes for them here instead of writing it. Every value starts as
/// the one the act replaced, so withdrawing or giving up the change always has a server value to return to, however
/// many observations arrived meanwhile. See
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>
/// axis F.
/// </para>
/// <para>
/// The record also says <see cref="MailboxMutationLocalChange.AlreadyCommitted" />, so a build that knows no overlay
/// still carries it to the server and still refuses to withdraw it, rather than reading a value it cannot parse.
/// </para>
/// </remarks>
public sealed record MailboxMutationOverlay(
    bool? ObservedSeenState,
    bool? ObservedFlaggedState,
    EmailKeywords? ObservedKeywords,
    MailFolderId? ObservedFolder,
    StoredEmailId? LocalCopy);
