// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.AutomaticReplies;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.AutomaticReplies;

/// <summary>What an arrived message's header block says about whether, and where, it may be answered automatically.</summary>
/// <param name="ReplyTarget">The address an answer goes to — the first <c>Reply-To</c> mailbox, else the first <c>From</c> one — or <see langword="null" /> where neither names one.</param>
/// <param name="Automation">What the message claimed about having been sent by a program or a list.</param>
/// <param name="HasNullReturnPath">Whether <c>Return-Path</c> names the null sender, which is how a bounce or a notification is sent.</param>
/// <param name="Subject">The subject it carried, or <see langword="null" /> where it carried none.</param>
/// <param name="ThreadReferences">The identifiers that place it in a conversation, which an answer continues.</param>
public sealed record ArrivedMailEnvelope(
    EmailAddress? ReplyTarget,
    EmailAutomation Automation,
    bool HasNullReturnPath,
    string? Subject,
    EmailThreadReferences ThreadReferences)
{
    /// <summary>Answers whether RFC 3834 lets the message be answered, and names where the answer goes when it does.</summary>
    /// <param name="ownAddress">The address the receiving account sends from, which is never answered.</param>
    /// <param name="target">The address to answer, when the answer is <see langword="true" />.</param>
    /// <returns>
    /// <see langword="false" /> for a message a program or a list sent, one sent from the null sender, one naming nobody
    /// to answer, one from an address nobody reads, and one the account sent itself.
    /// </returns>
    public bool TryReadAnswerable(EmailAddress ownAddress, out EmailAddress target)
    {
        target = default;

        if (this.Automation is not EmailAutomation.None
            || this.HasNullReturnPath
            || this.ReplyTarget is not { } candidate
            || AutomaticReplyRecipientPolicy.IsUnattended(candidate)
            || string.Equals(candidate.NormalizedAddress, ownAddress.NormalizedAddress, StringComparison.Ordinal))
        {
            return false;
        }

        target = candidate;

        return true;
    }
}
