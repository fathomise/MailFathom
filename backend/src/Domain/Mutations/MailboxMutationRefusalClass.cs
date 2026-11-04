// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Mutations;

/// <summary>Names whether a mail server's refusal of a change could come out differently if the change were asked again.</summary>
/// <remarks>
/// The class decides how the change ends and what the person is told. A permanent refusal ends the change at the first
/// one and is explained without an offer to retry; a transient one is asked again until the refusal window has passed
/// since the first, and its notification offers the act again. See
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>
/// axis G.
/// </remarks>
public enum MailboxMutationRefusalClass
{
    /// <summary>The server may take the change later: it was busy, over a limit, or answered <c>NO</c> without a reason the adapter can read.</summary>
    Transient = 0,

    /// <summary>Retrying cannot help: the folder or the message is gone, the folder is read-only, or the command was one the server should not have been sent.</summary>
    Permanent = 1,
}
