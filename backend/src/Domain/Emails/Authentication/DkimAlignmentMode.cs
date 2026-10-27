// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>The DKIM alignment a domain's DMARC record asks for, which is its <c>adkim</c> tag.</summary>
/// <remarks>
/// It narrows the relation MailFathom aligns by and never widens it. Relaxed keeps the relation — the signing name equal
/// to the <c>From</c> domain, beneath it, or above it, at a label boundary — and strict keeps equality alone. RFC 9989's
/// organizational domain is not computed for either.
/// </remarks>
public enum DkimAlignmentMode
{
    /// <summary><c>adkim=r</c>, or no <c>adkim</c> tag at all, which is the default.</summary>
    Relaxed = 0,

    /// <summary><c>adkim=s</c>: only a signature for exactly the <c>From</c> domain is aligned.</summary>
    Strict = 1,
}
