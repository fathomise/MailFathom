// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>The stages an account's outbox holds, as a caller narrows a listing of it to one.</summary>
/// <remarks>
/// It is narrower than <see cref="SendEmailState" /> on purpose: a send that left or was withdrawn is no longer in the
/// outbox, so a stage the listing could never answer with is not one the argument offers. The spellings are the ones
/// <see cref="SendEmailState" /> publishes for the same stages, so a caller filters by the value it reads back.
/// </remarks>
internal enum OutboxStage
{
    /// <summary>Written down and waiting to be offered to a mail server.</summary>
    [Description("Waiting to be offered to a mail server: about to be claimed, waiting out a retry, or held for the time it was written to leave at.")]
    Queued = 0,

    /// <summary>Begun to go out, with the server's answer not yet read.</summary>
    [Description("Begun to go out with no answer read yet: being transmitted now, or stopped mid-attempt so that nobody knows whether it arrived.")]
    Sending = 1,

    /// <summary>Refused by a mail server or out of attempts, and kept until a person decides about it.</summary>
    [Description("Refused by a mail server, or out of attempts, and kept until a person decides about it.")]
    Refused = 2,
}
