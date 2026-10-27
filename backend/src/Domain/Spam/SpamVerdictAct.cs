// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names one thing a spam verdict of this deployment did to a message in its mailbox.</summary>
/// <remarks>
/// <para>
/// It is recorded once per message and act, whatever custody phase the account was in when the act was taken, and it
/// outlives the verdict that took it: a reclassification replaces the verdict and leaves the record of what the earlier
/// one did. Two rules read it, and both need the same answer on a mirrored account, where the act also left a mutation
/// record, and on a held one, where it left nothing else.
/// </para>
/// <para>
/// The first rule is that MailFathom never reads its own act back as evidence: a junk-folder placement or a junk keyword
/// this deployment produced is not a signal the next classification of the same message may rest on. The second is that
/// an act is not repeated once somebody undid it: a message moved back out of the junk folder is not filed again, and a
/// keyword taken off is not written again.
/// </para>
/// </remarks>
public enum SpamVerdictAct
{
    /// <summary>A verdict filed the message into the account's junk destination.</summary>
    FiledIntoJunk = 0,

    /// <summary>A verdict added the <c>$Junk</c> keyword.</summary>
    MarkedJunk = 1,

    /// <summary>A verdict added the <c>$Phishing</c> keyword.</summary>
    MarkedPhishing = 2,

    /// <summary>A verdict took off a <c>$Junk</c> that stood beside a <c>$NotJunk</c>, as RFC 9051 asks of a client.</summary>
    /// <remarks>Recorded so the removal is asked for once per message in every custody phase, as an addition is.</remarks>
    ClearedJunkMarks = 3,
}
