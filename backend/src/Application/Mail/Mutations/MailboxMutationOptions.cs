// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Bounds how long a change the mail server keeps declining is asked again before it is given up on.</summary>
public sealed class MailboxMutationOptions
{
    /// <summary>Gets or sets how long after the server first declined a change, in a way asking again might alter, it is given up on.</summary>
    /// <remarks>
    /// <para>
    /// It is a period rather than an attempt count, because what a person waits on is time: a change the server keeps
    /// refusing reaches them, undone and explained, no later than this after the first refusal, however the account's
    /// runs were spaced in between. Only an answer starts it — a server that could not be reached was asked nothing, so an
    /// outage of any length neither starts nor spends it, and a refusal that turns out permanent ends the change at once.
    /// </para>
    /// <para>
    /// The deadline is kept by the account's supervision without opening a connection, so neither a backoff nor an outage
    /// that follows the refusal defers it. See
    /// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>
    /// axis G.
    /// </para>
    /// </remarks>
    public TimeSpan RefusalWindow { get; set; } = TimeSpan.FromMinutes(15);
}
