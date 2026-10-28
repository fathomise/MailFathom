// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What tied a trusted message to the address or domain a source recognized.</summary>
/// <remarks>
/// A contact, the owner's address, and the owner's domain are something somebody wrote down, so the message has to be
/// tied to it before it counts. Authentication is asked first, and the author's own signature is named only where
/// authentication did not hold, so the record says what the mark actually rested on.
/// </remarks>
public enum TrustedAuthorEvidence
{
    /// <summary>Nothing was asked of the message: every verdict that is not trusted, and every one the organization's signature reached.</summary>
    None = 0,

    /// <summary>The receiving server's DMARC <c>pass</c> for the author's domain, or a DKIM signature aligned with it, established the author.</summary>
    Authentication = 1,

    /// <summary>The message's own S/MIME or OpenPGP signature verified and names the author, where authentication did not hold.</summary>
    AuthorSignature = 2,
}
