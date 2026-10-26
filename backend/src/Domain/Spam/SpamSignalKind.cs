// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names what kind of fact one signal states about a message.</summary>
/// <remarks>
/// Each kind is recorded as itself rather than folded into one number, because the kinds are not comparable: an
/// authentication result is what the receiving server established at the moment it mattered, a provider score is that
/// provider's own opinion, and a folder placement is a decision somebody already acted on. A reader diagnosing a wrong
/// classification needs to know which of them it rested on. <c>4</c> is absent because the kind that held it went with
/// the stage that reported it, and the number is not reissued.
/// </remarks>
public enum SpamSignalKind
{
    /// <summary>An SPF, DKIM, or DMARC result the receiving server recorded for the message it accepted.</summary>
    SenderAuthentication = 0,

    /// <summary>The same kind of result preserved across a forwarding hop, where SPF and DKIM legitimately break.</summary>
    ForwardedSenderAuthentication = 1,

    /// <summary>A spam verdict, flag, or score the provider wrote into the message before MailFathom saw it.</summary>
    ProviderSpamVerdict = 2,

    /// <summary>The message being stored in the folder the account advertises as its junk folder.</summary>
    JunkFolderPlacement = 3,

    /// <summary>A decision model's judgement of the message: the verdict it reached, or the subcategory it named.</summary>
    /// <remarks>
    /// Its provenance names the model build that answered and the revision of the instructions it was asked under, so a
    /// questioned verdict can be traced to the exact call that produced it rather than to whatever is configured now.
    /// </remarks>
    ModelJudgement = 5,
}
