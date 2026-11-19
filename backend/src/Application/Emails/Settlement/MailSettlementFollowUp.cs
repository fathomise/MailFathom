// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Settlement;

/// <summary>What the settlement does with a batch once it has settled, each of which may fail without undoing the settlement.</summary>
public enum MailSettlementFollowUp
{
    /// <summary>The automatic reply answering what settled.</summary>
    AutomaticReply,

    /// <summary>The closing of the waits that what settled answers.</summary>
    AwaitedReplies,
}
