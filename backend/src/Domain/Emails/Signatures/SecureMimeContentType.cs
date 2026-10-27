// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Signatures;

/// <summary>The <c>smime-type</c> an opaque S/MIME part declares, recorded as declared.</summary>
/// <remarks>Only <see cref="SignedData" /> is verified; the enveloped and compressed types are recorded and not opened here.</remarks>
public enum SecureMimeContentType
{
    /// <summary><c>signed-data</c>: the part carries the signed content and its signature together.</summary>
    SignedData = 0,

    /// <summary><c>enveloped-data</c>: the content is encrypted.</summary>
    EnvelopedData = 1,

    /// <summary><c>authEnveloped-data</c>: the content is encrypted with authenticated encryption.</summary>
    AuthEnvelopedData = 2,

    /// <summary><c>compressed-data</c>: the content is compressed.</summary>
    CompressedData = 3,

    /// <summary><c>certs-only</c>: the part carries certificates and signs nothing.</summary>
    CertsOnly = 4,

    /// <summary>The part declares no <c>smime-type</c>, which older senders omit; it is read as signed data.</summary>
    NotDeclared = 5,
}
