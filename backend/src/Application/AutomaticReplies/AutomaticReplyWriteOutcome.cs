// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.AutomaticReplies;

/// <summary>Whether a user's automatic reply was saved, and why not where it was not.</summary>
public enum AutomaticReplyWriteOutcome
{
    /// <summary>The reply was saved under a new revision.</summary>
    Saved = 0,

    /// <summary>An account the reply names is not assigned to the user.</summary>
    AccountNotAssigned = 1,

    /// <summary>An account the reply names is a shared mailbox, which is never one person's to answer for.</summary>
    AccountShared = 2,

    /// <summary>The reply carries a text for senders inside the organization, and the user is a member of none.</summary>
    NoOrganization = 3,

    /// <summary>This deployment holds no record for the user.</summary>
    UserUnknown = 4,
}
