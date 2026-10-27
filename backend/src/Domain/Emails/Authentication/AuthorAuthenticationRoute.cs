// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What established an authenticated author, which decides what the author is worth to a trust source.</summary>
/// <remarks>
/// <para>
/// Every route reaches <see cref="AuthorAuthenticationOutcome.Authenticated" />, and they are recorded apart because
/// they are not worth the same. A trust source that needs an authenticated author accepts <see cref="DmarcPass" /> and
/// <see cref="AlignedDkim" /> only: SPF authenticates whoever handed the message over, and a shared relay or provider
/// satisfies it for everybody using it.
/// </para>
/// <para>
/// Where several routes hold, the first of <see cref="DmarcPass" />, <see cref="AlignedDkim" />, and
/// <see cref="AlignedSpf" /> is recorded, so the record names the strongest claim the message supports.
/// </para>
/// </remarks>
public enum AuthorAuthenticationRoute
{
    /// <summary>Nothing established the author, so no route is recorded.</summary>
    None = 0,

    /// <summary>The trusted receiving server reported DMARC <c>pass</c> for the domain of the first <c>From</c> mailbox.</summary>
    DmarcPass = 1,

    /// <summary>A verified DKIM signature is aligned with the <c>From</c> domain under that domain's own DMARC record.</summary>
    AlignedDkim = 2,

    /// <summary>Only an SPF identity the receiving server checked lines up with the <c>From</c> domain.</summary>
    AlignedSpf = 3,
}
