// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Infrastructure.Certificates;

/// <summary>Reads the mailbox addresses a certificate names, which is what both signing and verifying match an author against.</summary>
/// <remarks>
/// RFC 8550 § 3 asks a receiving agent to recognize both the <c>rfc822Name</c> alternative names and the legacy
/// <c>emailAddress</c> subject attribute. The platform publishes no reader for an <c>rfc822Name</c>, so the extension is
/// decoded here; a malformed extension or subject names nothing rather than failing, because the certificate may come
/// from whoever sent the message.
/// </remarks>
internal static class CertificateMailAddresses
{
    private const string SubjectAlternativeNameOid = "2.5.29.17";

    private const string EmailAddressAttributeOid = "1.2.840.113549.1.9.1";

    private static readonly Asn1Tag Rfc822NameTag = new(TagClass.ContextSpecific, 1);

    /// <summary>Lists every address the certificate names: its alternative names first, then its subject.</summary>
    /// <param name="certificate">The certificate to read.</param>
    /// <returns>The addresses, possibly empty, exactly as the certificate spells them.</returns>
    public static string[] Of(X509Certificate2 certificate) =>
        [.. AlternativeAddressesOf(certificate), .. SubjectAddressesOf(certificate)];

    private static string[] SubjectAddressesOf(X509Certificate2 certificate)
    {
        try
        {
            return
            [
                .. certificate.SubjectName.EnumerateRelativeDistinguishedNames()
                    .Where(static name => !name.HasMultipleElements
                        && name.GetSingleElementType().Value == EmailAddressAttributeOid)
                    .Select(static name => name.GetSingleElementValue())
                    .OfType<string>(),
            ];
        }
        catch (CryptographicException)
        {
            return [];
        }
    }

    private static string[] AlternativeAddressesOf(X509Certificate2 certificate)
    {
        if (certificate.Extensions[SubjectAlternativeNameOid] is not { } declaredNames)
        {
            return [];
        }

        var addresses = new List<string>();
        try
        {
            var generalNames = new AsnReader(declaredNames.RawData, AsnEncodingRules.DER).ReadSequence();

            while (generalNames.HasData)
            {
                if (generalNames.PeekTag() == Rfc822NameTag)
                {
                    addresses.Add(generalNames.ReadCharacterString(UniversalTagNumber.IA5String, Rfc822NameTag));
                }
                else
                {
                    generalNames.ReadEncodedValue();
                }
            }
        }
        catch (AsnContentException)
        {
            return [];
        }

        return [.. addresses];
    }
}
