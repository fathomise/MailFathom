// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Provenance;

/// <summary>Names where a message the mailbox has not sent stands, which is what the person's own Agent is told about it.</summary>
/// <remarks>
/// None of these is mail anybody received. They differ in what the person can expect of it: a draft goes nowhere until
/// they ask, a scheduled send leaves at its instant, a send in progress leaves as soon as a server takes it, and a failed
/// one goes nowhere until they act on it.
/// </remarks>
public enum UnsentEmailState
{
    /// <summary>A working copy nobody has asked to send.</summary>
    Draft = 0,

    /// <summary>A send held until a due instant still ahead.</summary>
    Scheduled = 1,

    /// <summary>A send asked to leave now that has not been transmitted yet, a send a server deferred, and an attempt whose answer never came back.</summary>
    Sending = 2,

    /// <summary>A send that will not be offered again, so the person has to act on it.</summary>
    Failed = 3,
}
