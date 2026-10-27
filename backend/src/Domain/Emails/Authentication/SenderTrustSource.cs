// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Which part of what this deployment knows recognized an authenticated author.</summary>
/// <remarks>
/// A verdict that says only <em>trusted</em> cannot be reviewed. Trust an operator declared when they set the
/// deployment up, trust that follows from the accounts they synchronize, and trust somebody granted while it was running
/// are three different statements, and the last of them is the one worth being able to find again — particularly where
/// the somebody was an agent. So the verdict names the half that matched rather than only the fact that one did.
/// </remarks>
public enum SenderTrustSource
{
    /// <summary>Nothing recognized the author, which is what every <see cref="SenderTrustLevel.Unknown" /> verdict carries.</summary>
    None = 0,

    /// <summary>The author's domain is the domain of an account the receiving account's owner holds, and the receiving account opted into those domains.</summary>
    /// <remarks>
    /// Every one of the owner's accounts' domains counts and not only the receiving one, because a person holding a
    /// work mailbox and a personal one holds one person's correspondence. It is off unless an account asks for it,
    /// because on a large shared provider the owner's domain is every stranger's domain too.
    /// </remarks>
    OwnAccountDomain = 1,

    /// <summary>An entry on the receiving account's configured trusted-sender list named the author.</summary>
    ConfiguredTrustedSender = 2,

    /// <summary>An entry somebody added to the receiving account's stored trusted-sender list named the author.</summary>
    /// <remarks>
    /// The stored half is what a reader adds without editing a file and restarting. Where both halves name the same
    /// author the configured one is reported, so a deployment's declared trust is never described as something added
    /// later.
    /// </remarks>
    StoredTrustedSender = 3,

    /// <summary>A signature made with a key of the receiving account's organization proved the author a member of it.</summary>
    /// <remarks>
    /// It ranks first and needs no authenticated author domain, because the signature proves the address itself rather
    /// than a domain: a colleague on a server that publishes no DKIM key is still proven, and one on the organization's
    /// own domain is named as a member rather than by that domain.
    /// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md">ADR 0042</see>
    /// states the three conditions it is granted under.
    /// </remarks>
    OrganizationMember = 4,

    /// <summary>The author wrote as exactly one of the addresses the receiving account's owner holds.</summary>
    /// <remarks>
    /// It is mail from the owner to themselves, so it needs no configuration. It is held against an authenticated
    /// author exactly as an address entry on the trusted-sender list is: the author's domain is the address's domain and
    /// <c>From</c> shows that address, and where nothing authenticated the author it recognizes nothing.
    /// </remarks>
    OwnAccountAddress = 5,
}
