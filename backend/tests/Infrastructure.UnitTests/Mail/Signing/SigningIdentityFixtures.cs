// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.UnitTests.Mail.Signing;

/// <summary>Fixed S/MIME certificates and OpenPGP keys, generated once with OpenSSL and GnuPG and committed, so no test generates a key.</summary>
/// <remarks>
/// <para>
/// Every identity here is a throwaway made for these tests: it names only addresses in the reserved <c>example.test</c>
/// domain, and its private key protects nothing. The validity periods are wide enough that a test fixing its clock
/// inside 2026–2035 reads every certificate as current, and moves the clock to read one as not yet valid or expired.
/// </para>
/// <para>One certification was made with BouncyCastle instead, where <see cref="OpenPgpRecertifiedUserId" /> says why.</para>
/// </remarks>
internal static class SigningIdentityFixtures
{
    /// <summary>The authority that issued every signing certificate here, valid from 2026-01-01 to 2046-01-01.</summary>
    internal const string Authority = """
        -----BEGIN CERTIFICATE-----
        MIIBvjCCAWOgAwIBAgIUEFqOOE2VPPX7XGBHJcMIC6+UMi0wCgYIKoZIzj0EAwIw
        LDEqMCgGA1UEAwwhTWFpbEZhdGhvbSBUZXN0IFNpZ25pbmcgQXV0aG9yaXR5MB4X
        DTI2MDEwMTAwMDAwMFoXDTQ2MDEwMTAwMDAwMFowLDEqMCgGA1UEAwwhTWFpbEZh
        dGhvbSBUZXN0IFNpZ25pbmcgQXV0aG9yaXR5MFkwEwYHKoZIzj0CAQYIKoZIzj0D
        AQcDQgAEs0qxmwgby5NIAsOLtJv1ZfTv4O2ZyngGLtB5Nppb4Z/x9/jBkhqI2Zuk
        rYJMJxrv/M/631nUsPp2zZTIGjTQ9aNjMGEwHQYDVR0OBBYEFFEV5eF+AI7CRuCm
        wXXmEs9kVjM6MB8GA1UdIwQYMBaAFFEV5eF+AI7CRuCmwXXmEs9kVjM6MA8GA1Ud
        EwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMAoGCCqGSM49BAMCA0kAMEYCIQCQ
        OcJJ5V3ARvs1YgF0dqBCOmc5kptyjPwVqaUnlel3+gIhAPUIR5WMf3ftN+NUulqP
        iFkS0CTlOXLBxTdrLlazQffc
        -----END CERTIFICATE-----
        """;

    /// <summary>A certificate valid from 2026-01-01 to 2036-01-01 that names <c>sender@example.test</c>, permits <c>digitalSignature</c> and email protection.</summary>
    internal const string SignerCertificate = """
        -----BEGIN CERTIFICATE-----
        MIIByzCCAXGgAwIBAgIJAL+IiIe2A/8tMAoGCCqGSM49BAMCMCwxKjAoBgNVBAMM
        IU1haWxGYXRob20gVGVzdCBTaWduaW5nIEF1dGhvcml0eTAeFw0yNjAxMDEwMDAw
        MDBaFw0zNjAxMDEwMDAwMDBaMBExDzANBgNVBAMMBlNlbmRlcjBZMBMGByqGSM49
        AgEGCCqGSM49AwEHA0IABJNBlHZijpQTQ+N5uTf8kxGBYonuttwOKNzO+ujBpsle
        HdOUZrp470fy8TLvmh8YAKQMt+7SVfN05HY6gKr1bn6jgZYwgZMwDAYDVR0TAQH/
        BAIwADAeBgNVHREEFzAVgRNzZW5kZXJAZXhhbXBsZS50ZXN0MA4GA1UdDwEB/wQE
        AwIHgDATBgNVHSUEDDAKBggrBgEFBQcDBDAdBgNVHQ4EFgQUbPX929UzoqHJK3dQ
        FGSiR3NRTqEwHwYDVR0jBBgwFoAUURXl4X4AjsJG4KbBdeYSz2RWMzowCgYIKoZI
        zj0EAwIDSAAwRQIgZ+Kn9aZPD2zFrNywUuOhGeYlbYNOPrTWhqBnN97M2aACIQDj
        PGXORnOynNZaWk4jGjkjD9/BT5jkO0Ho03hVIb6slg==
        -----END CERTIFICATE-----
        """;

    /// <summary>The unencrypted PKCS#8 key of <see cref="SignerCertificate" />.</summary>
    internal const string SignerPrivateKey = """
        -----BEGIN PRIVATE KEY-----
        MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgo5Hn6RoG6+yxQ/uB
        fM8ams8+Zy/UXu2rIJ4VqzjopVahRANCAASTQZR2Yo6UE0Pjebk3/JMRgWKJ7rbc
        DijczvrowabJXh3TlGa6eO9H8vEy75ofGACkDLfu0lXzdOR2OoCq9W5+
        -----END PRIVATE KEY-----
        """;

    /// <summary>A certificate valid from 2026-01-01 to 2036-01-01 that permits <c>keyAgreement</c> alone, and nothing that signs.</summary>
    internal const string KeyAgreementOnlyCertificate = """
        -----BEGIN CERTIFICATE-----
        MIIByTCCAXCgAwIBAgIIFhBk5TGZIC8wCgYIKoZIzj0EAwIwLDEqMCgGA1UEAwwh
        TWFpbEZhdGhvbSBUZXN0IFNpZ25pbmcgQXV0aG9yaXR5MB4XDTI2MDEwMTAwMDAw
        MFoXDTM2MDEwMTAwMDAwMFowETEPMA0GA1UEAwwGU2VuZGVyMFkwEwYHKoZIzj0C
        AQYIKoZIzj0DAQcDQgAEWOJe5wuaHE6GIDxfK55XcZyHxMsWq7sZP96YhiCeKT9A
        6/+OdNJU9+803PVJWth2qOEvoQZhN9/rLxMbybNhuqOBljCBkzAMBgNVHRMBAf8E
        AjAAMB4GA1UdEQQXMBWBE3NlbmRlckBleGFtcGxlLnRlc3QwDgYDVR0PAQH/BAQD
        AgMIMBMGA1UdJQQMMAoGCCsGAQUFBwMEMB0GA1UdDgQWBBSfNBia0LMoNNh4kd/w
        qX6ic6PepTAfBgNVHSMEGDAWgBRRFeXhfgCOwkbgpsF15hLPZFYzOjAKBggqhkjO
        PQQDAgNHADBEAiB99RurVqBq5+UL+5fMkbWfnTORy0Hj3zsdR+rm5EDUCQIgAi06
        ZZM61LQvyAfYXz9vlz5DmE4vjnaEOKDdDtfTmL4=
        -----END CERTIFICATE-----
        """;

    /// <summary>The unencrypted PKCS#8 key of <see cref="KeyAgreementOnlyCertificate" />.</summary>
    internal const string KeyAgreementOnlyPrivateKey = """
        -----BEGIN PRIVATE KEY-----
        MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgDOEWd0Fjh6yVjsef
        hbUmAeeJo+apPZbnWSDBjHAl1smhRANCAARY4l7nC5ocToYgPF8rnldxnIfEyxar
        uxk/3piGIJ4pP0Dr/4500lT37zTc9Ula2Hao4S+hBmE33+svExvJs2G6
        -----END PRIVATE KEY-----
        """;

    /// <summary>A certificate valid from 2026-01-01 to 2036-01-01 that permits <c>nonRepudiation</c> alone, which RFC 8550 accepts for signing.</summary>
    internal const string NonRepudiationOnlyCertificate = """
        -----BEGIN CERTIFICATE-----
        MIIByzCCAXGgAwIBAgIJAP87UZhHDx+3MAoGCCqGSM49BAMCMCwxKjAoBgNVBAMM
        IU1haWxGYXRob20gVGVzdCBTaWduaW5nIEF1dGhvcml0eTAeFw0yNjAxMDEwMDAw
        MDBaFw0zNjAxMDEwMDAwMDBaMBExDzANBgNVBAMMBlNlbmRlcjBZMBMGByqGSM49
        AgEGCCqGSM49AwEHA0IABMO03uMn/PhPN5JYW4FpBtlgOikLk/oRPbWjEf9WznVy
        F6SZfIzObXb5/8ggCWC4D9Y/gglJAsPwUIZ9+yLGpzqjgZYwgZMwDAYDVR0TAQH/
        BAIwADAeBgNVHREEFzAVgRNzZW5kZXJAZXhhbXBsZS50ZXN0MA4GA1UdDwEB/wQE
        AwIGQDATBgNVHSUEDDAKBggrBgEFBQcDBDAdBgNVHQ4EFgQUdJ3aTEoVt3R+tWvc
        CbO7qLHZTe4wHwYDVR0jBBgwFoAUURXl4X4AjsJG4KbBdeYSz2RWMzowCgYIKoZI
        zj0EAwIDSAAwRQIhAJKX3URZAEG3Wd/BTKofSrQhHQHLCON5ipio7e/XjLVvAiBe
        iW1CsRlCkHOa9q0tpLDlYaAVEsKKD00wJX6GMlCVLw==
        -----END CERTIFICATE-----
        """;

    /// <summary>The unencrypted PKCS#8 key of <see cref="NonRepudiationOnlyCertificate" />.</summary>
    internal const string NonRepudiationOnlyPrivateKey = """
        -----BEGIN PRIVATE KEY-----
        MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgD7tWERWR1YkEYUz6
        SXlJn1Q5d2/ZCUJTTPja0h48TgOhRANCAATDtN7jJ/z4TzeSWFuBaQbZYDopC5P6
        ET21oxH/Vs51chekmXyMzm12+f/IIAlguA/WP4IJSQLD8FCGffsixqc6
        -----END PRIVATE KEY-----
        """;

    /// <summary>A certificate valid from 2026-01-01 to 2036-01-01 that is restricted to server authentication.</summary>
    internal const string ServerAuthenticationOnlyCertificate = """
        -----BEGIN CERTIFICATE-----
        MIIByzCCAXGgAwIBAgIJANxfoD4arY0rMAoGCCqGSM49BAMCMCwxKjAoBgNVBAMM
        IU1haWxGYXRob20gVGVzdCBTaWduaW5nIEF1dGhvcml0eTAeFw0yNjAxMDEwMDAw
        MDBaFw0zNjAxMDEwMDAwMDBaMBExDzANBgNVBAMMBlNlbmRlcjBZMBMGByqGSM49
        AgEGCCqGSM49AwEHA0IABCf1JcYQEpHPa8S6kCifP3wnK0wvARMQoDNhhWUk71uv
        bDXMHkP/DQGv8uR7HAoDy5POsikknwYtJGWLr0FWMfSjgZYwgZMwDAYDVR0TAQH/
        BAIwADAeBgNVHREEFzAVgRNzZW5kZXJAZXhhbXBsZS50ZXN0MA4GA1UdDwEB/wQE
        AwIHgDATBgNVHSUEDDAKBggrBgEFBQcDATAdBgNVHQ4EFgQUbnAxP+Hwr92zQpFF
        cgp2vq4T+9kwHwYDVR0jBBgwFoAUURXl4X4AjsJG4KbBdeYSz2RWMzowCgYIKoZI
        zj0EAwIDSAAwRQIhALKDJxOwIgYtHJ3ROKIJrkOON/rhhV0cQToU7bDIS6wBAiAj
        eCHNLXH/5EKGt9RQsQlf8x5NJENjFNoIoOU61JLNMw==
        -----END CERTIFICATE-----
        """;

    /// <summary>The unencrypted PKCS#8 key of <see cref="ServerAuthenticationOnlyCertificate" />.</summary>
    internal const string ServerAuthenticationOnlyPrivateKey = """
        -----BEGIN PRIVATE KEY-----
        MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQge2xU+sEPIaHAxPpx
        uO/bQ4pTtxCRzx+teUaw8HWsHo+hRANCAAQn9SXGEBKRz2vEupAonz98JytMLwET
        EKAzYYVlJO9br2w1zB5D/w0Br/LkexwKA8uTzrIpJJ8GLSRli69BVjH0
        -----END PRIVATE KEY-----
        """;

    /// <summary>A certificate valid from 2026-01-01 to 2036-01-01 that names <c>someone.else@example.test</c> only.</summary>
    internal const string OtherAddressCertificate = """
        -----BEGIN CERTIFICATE-----
        MIIB1jCCAXygAwIBAgIISC6qhMNKhf8wCgYIKoZIzj0EAwIwLDEqMCgGA1UEAwwh
        TWFpbEZhdGhvbSBUZXN0IFNpZ25pbmcgQXV0aG9yaXR5MB4XDTI2MDEwMTAwMDAw
        MFoXDTM2MDEwMTAwMDAwMFowFzEVMBMGA1UEAwwMU29tZW9uZSBFbHNlMFkwEwYH
        KoZIzj0CAQYIKoZIzj0DAQcDQgAEn/u8HN22+iDwQAPglDHFT3q1oY5jW3DriXB3
        idkrRyHT2ki2bLf/FjVAxgxMFyLfLxGUAyVUl2ERcd+cgpY9AKOBnDCBmTAMBgNV
        HRMBAf8EAjAAMCQGA1UdEQQdMBuBGXNvbWVvbmUuZWxzZUBleGFtcGxlLnRlc3Qw
        DgYDVR0PAQH/BAQDAgeAMBMGA1UdJQQMMAoGCCsGAQUFBwMEMB0GA1UdDgQWBBTr
        LCUxi66OmTtWymoK4gOGR8x+qjAfBgNVHSMEGDAWgBRRFeXhfgCOwkbgpsF15hLP
        ZFYzOjAKBggqhkjOPQQDAgNIADBFAiBR8UYKO46YTZvm6Ysd969VE6SvEKYmejjB
        LHvsdlHpCQIhAOzZ1FL+s+7YBT6ATQXLF4zNwvWqCxwRVdXJtzjp6+RV
        -----END CERTIFICATE-----
        """;

    /// <summary>The unencrypted PKCS#8 key of <see cref="OtherAddressCertificate" />.</summary>
    internal const string OtherAddressPrivateKey = """
        -----BEGIN PRIVATE KEY-----
        MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgQiATfRzbuy29OMeW
        fSKeofu6JlxmS46VzTF+RPrePTehRANCAASf+7wc3bb6IPBAA+CUMcVPerWhjmNb
        cOuJcHeJ2StHIdPaSLZst/8WNUDGDEwXIt8vEZQDJVSXYRFx35yClj0A
        -----END PRIVATE KEY-----
        """;

    /// <summary>A certificate valid from 2026-01-01 to 2036-01-01 that names <c>sender@example.test</c> in its subject and has no alternative name.</summary>
    internal const string SubjectEmailCertificate = """
        -----BEGIN CERTIFICATE-----
        MIIBzTCCAXKgAwIBAgIIIL2/AC8J1YowCgYIKoZIzj0EAwIwLDEqMCgGA1UEAwwh
        TWFpbEZhdGhvbSBUZXN0IFNpZ25pbmcgQXV0aG9yaXR5MB4XDTI2MDEwMTAwMDAw
        MFoXDTM2MDEwMTAwMDAwMFowNTEPMA0GA1UEAwwGU2VuZGVyMSIwIAYJKoZIhvcN
        AQkBFhNzZW5kZXJAZXhhbXBsZS50ZXN0MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcD
        QgAEA69h/i+dxAIO9ChpIKJbaG41XFyqYjzWBGgck0Yyhd/qCW9o7HNp+bwNmDuD
        BjZEUoi2P7rtIeExmXRbTYwYvqN1MHMwDAYDVR0TAQH/BAIwADAOBgNVHQ8BAf8E
        BAMCB4AwEwYDVR0lBAwwCgYIKwYBBQUHAwQwHQYDVR0OBBYEFHucN2pEIclBuZ5O
        wSVKuOweuhagMB8GA1UdIwQYMBaAFFEV5eF+AI7CRuCmwXXmEs9kVjM6MAoGCCqG
        SM49BAMCA0kAMEYCIQCmX02t2shATU+KUgDIPeTI2GsFMVxr8ZXD7pt3mlUYjwIh
        AL5GP+lk4bre3b2GzP7heY80WHDznUj7bBHhWwBOAxte
        -----END CERTIFICATE-----
        """;

    /// <summary>The unencrypted PKCS#8 key of <see cref="SubjectEmailCertificate" />.</summary>
    internal const string SubjectEmailPrivateKey = """
        -----BEGIN PRIVATE KEY-----
        MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgG385eeSLnRulqgUf
        GAnVCFN24RXXSqIYmRktx6qomnOhRANCAAQDr2H+L53EAg70KGkgoltobjVcXKpi
        PNYEaByTRjKF3+oJb2jsc2n5vA2YO4MGNkRSiLY/uu0h4TGZdFtNjBi+
        -----END PRIVATE KEY-----
        """;

    /// <summary>The key of <see cref="SignerCertificate" />, encrypted with <see cref="SignerPrivateKeyPassword" />.</summary>
    internal const string SignerEncryptedPrivateKey = """
        -----BEGIN ENCRYPTED PRIVATE KEY-----
        MIH0MF8GCSqGSIb3DQEFDTBSMDEGCSqGSIb3DQEFDDAkBBCxeuteDpPfVyYJaudQ
        kYjqAgIIADAMBggqhkiG9w0CCQUAMB0GCWCGSAFlAwQBKgQQvJvlmCUhnclOUeN+
        soPHYwSBkODtKT6vSxmCxLrjrhyVhIDxgggnL3HAquhRk9ht8FtXjTYthiskKPY0
        VHcK/Ph5rvWrUirwK1j52ZatiUVYUIHYCvnJHZWup+KTvi+vIa154ocS6ChOBiWJ
        Qc4mKCiGRiOAnEJ2VJ/TvXnY1FccnstXKHaTXHnnjtI/ZLMU8j0eYIlzJv/5wMEh
        uCazCrXbbA==
        -----END ENCRYPTED PRIVATE KEY-----
        """;

    /// <summary>The password <see cref="SignerEncryptedPrivateKey" /> is encrypted with.</summary>
    internal const string SignerPrivateKeyPassword = "signing-key-password";

    /// <summary>A PKCS#12 bundle of <see cref="SignerCertificate" />, its key, and <see cref="Authority" />, protected with <see cref="SignerBundlePassword" />.</summary>
    internal static readonly byte[] SignerBundle = Convert.FromBase64String(
            "MIIGXAIBAzCCBhIGCSqGSIb3DQEHAaCCBgMEggX/MIIF+zCCBKoGCSqGSIb3DQEHBqCCBJswggSXAgEAMIIEkAYJKoZIhvcNAQcB"
            + "MF8GCSqGSIb3DQEFDTBSMDEGCSqGSIb3DQEFDDAkBBCgla4TguDNS6dwnZ74C0+qAgIIADAMBggqhkiG9w0CCQUAMB0GCWCGSAFl"
            + "AwQBKgQQImvSbhc0sWPtDVLvHpUwSoCCBCA3jAt++ztEra6YR03UMlx3g8fJrO+INuzby5bJSxHJzSnWUXP7D2Lm0KBn4oCAxsOz"
            + "QZ2KpDDNWYaL2JfD8ReXuX1Iy42nYVEaOBijRDW3WdLxq5m0G/8nPIpYU9b4jZ0oMKnzMEfYsm+zEZivBzOw2lh0+tByCtfvbK8M"
            + "ZVaD99vCIB9I5VOkR52qu6fI27SrGYjMCMmmLGpjssFr8w9od73poYWXrNkO4YDOZyTjeLdvV9VzpEq2aIJC7LLGVQ7dV5AVd8xT"
            + "xyOokC1kZXkJaeca4QZLu1g4I3AQ3peYt7bGmjQl+dADr8SQiM128b2u5SEHIf827B17uB5cRkCNlOFIMV1SVIrWve3geVHkOblW"
            + "inlpA6hcAsGvc4mZ9VRGyU6AUXDcHKBj4XTbOFhEVxDs9fCBeqTZj1fI086fxhTtBDSl6jqKvIdsNz78Nl9wU09gYqZ54PITNDqv"
            + "hylJuU60xKKPHptG0gRrAkmzyN4UZG31+w2HyuUy7ZCCTwPVNLVMS7kgJ9Ipp1hxNBf+WILVsQsMFXOAczC/8Lk6pzeclq8ki+xF"
            + "R9T8xeFnA3eFJcqg/HETZEXKZ+doz6ONtLJKEJ11c3q86a8/SvWSO0Bgo2RbaUQvVM4iBx/UYprwIvmpXBZSCGIsKmMJJMhEMVm2"
            + "3GNPcIWPte7vwRy9OpV+7X9njvYTzCRIMeJxiiJkpSMR2dI/+zhzU7oCn51qhl/DnEAjendiZ/XYrko3HTEwNRz3sxyNjIkg8m8B"
            + "U4OEshNST9s4af4ySVChyaBrWg9gLMUmXnfB/ntCLQXCDJlZblgQEEaVjnx65hHLSyPZg7dlOhGiPBGKTUgdskxq8zhVbug3QqY8"
            + "IA7fzbGg/xVLJN0fUCdLrO/Q1ggcI/PK/328iOWbpxZkxgQ2PFKPzdwAxFVnaJ9TrpVG0P1lTk7bdevYmP7DAPFJ3MfQVDVxDiCG"
            + "YOgXyYPSJkKtC/rdLZ/wCx/Eto0aML1OnDc/Lj6bzWApMbIYs+jWBB4CGWPkeL1PDADkW+dzap8Rdcq9Q5KeEwTwvFSPu699JbwB"
            + "lAz13NZNc1PV05UbDHRn2phD7ga6e2xTGafIteLGuaar/dd4JyriQ4+t1LiEpnbqB1LETlsDNwxdMQ3e6ck0SdF9zs7aZFi0iqOt"
            + "9NZv2gU2idQ9bav0mNfSHMqn7ReFGVc5A1eN+hYEmQqxhzgEifkhzvwJEQ5RVohFOS+Vg48zu7TLNOhxZIt+ARP90wbtTZsmXmrc"
            + "lz37VV0vJUDvCkZOt/RlHnGsQpHjY4nXE24i7sDzoy8l/VTLG9YVlthOVo/Xa/tY9PlE+i+nZLuwZLUTG+EGA9pLtu33HuPfRFl1"
            + "AdxQ+PbsV/w4ysosDAEIZsOZSTHIp2f1mRHvxie6Qg8wggFJBgkqhkiG9w0BBwGgggE6BIIBNjCCATIwggEuBgsqhkiG9w0BDAoB"
            + "AqCB9zCB9DBfBgkqhkiG9w0BBQ0wUjAxBgkqhkiG9w0BBQwwJAQQdt0p1guZrdfDv8vM1JTYMQICCAAwDAYIKoZIhvcNAgkFADAd"
            + "BglghkgBZQMEASoEEOwIhmEfKMfwBrl41tX12scEgZBQ/pIbD3Y4LeuHwdI/rxQkk9sHgRsCueZaInQGIyN9j7oDkqNIA/cXURtr"
            + "g+V+lxSHw+ZsNNSykG9RgtKnB84Z2aPViVTFOvgiUmJxvJLqMmdwk/TaCvbpQhhCBPB9u5JTud3JuqJBd1G+UAP3uGSEy81v+dH1"
            + "pNRbD5goAfTdmITbWUgB6PhslMEYTLRJkjYxJTAjBgkqhkiG9w0BCRUxFgQU77ziPwTJxd8lBPoknH04NG3qGMgwQTAxMA0GCWCG"
            + "SAFlAwQCAQUABCD5eaDjryU021ESFZfEbuZE8ludtbHawPEmsj9mR6mevwQIa2TS16kYmuACAggA");

    /// <summary>The password <see cref="SignerBundle" /> is protected with.</summary>
    internal const string SignerBundlePassword = "signing-bundle-password";

    /// <summary>A PKCS#12 bundle holding <see cref="SignerCertificate" /> and no private key.</summary>
    internal static readonly byte[] SignerBundleWithoutKey = Convert.FromBase64String(
            "MIIC/wIBAzCCArUGCSqGSIb3DQEHAaCCAqYEggKiMIICnjCCApoGCSqGSIb3DQEHBqCCAoswggKHAgEAMIICgAYJKoZIhvcNAQcB"
            + "MF8GCSqGSIb3DQEFDTBSMDEGCSqGSIb3DQEFDDAkBBDJfVLFkQa4ag1MP2AXieMiAgIIADAMBggqhkiG9w0CCQUAMB0GCWCGSAFl"
            + "AwQBKgQQuVZup06ZotDeQMMxBI99doCCAhBwyzi591leYgwcyY+IswMWHfJbGDxIRO+Phv6hZRW7h7AG8qeIPYzybD0TH6Z2GDen"
            + "EaJgfeZbFcMz7e9D5DA1tjjVhbgt/FPJRxzU5Jo4ke28dygxjD3kgldOhXxXTsDyoMf5n3d0A0VijjfF1zCTW9sne0lefAv5V5we"
            + "gxEANYg29HeVuhacblG4G0fERjtsxKrlSSw7buOhzPPDpAvl9KQQfKxTXizB+5QG/bPW3TnqdbHvI7+3csh3i5tQFZKECHfyfM6r"
            + "Z2edsjKBlpkwZcW3g4Yf6DPadkCNIDvpoLXQnsVzuV07gsZQO1RlvcDU9JQ/2+lKdyHcEdUcPS+FXDfJPNzyZLIs3BcG/d4+A8pE"
            + "9qMtZjvmewwwV4gKBMBoXLwJX3uMkVXYcUIWF6rfB1FhkMvuOpJz6qqeTVOffnilmQj7TcaBH8PmqCLeZ9dENZ2/MnAS5qMzVXTz"
            + "VUw8kki2xFCLICymge0nrAITC+u6I2AyF/J4eKqwaOkxFnhRZ8Qwqd702rtvm/Nf33WUT8rM6nVkGWPCVMqLxzpnZLfGMzezJmR7"
            + "lnlRQh//tqE8GDpCQXWrJmDg+UkwQVe/Lj3G+xujKDAlXjkEf0uvMzQgWIJsEacPC4EK130k6yd8JdVIaswD1IXvAIeBkEIqxp6R"
            + "EsT1L9VBkJeMAHuWt7L8caSl3Mv89KNFLh7hOVkwQTAxMA0GCWCGSAFlAwQCAQUABCAywhjZP3mByY5muUGf559RVN76n4pAs3/N"
            + "GrRfm1hPLAQIQgY3F0U3mpgCAggA");

    /// <summary>A key for <c>sender@example.test</c> whose unprotected primary key certifies and signs, with an encryption subkey.</summary>
    internal const string OpenPgpSigner = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdA1st8ns5gRnDi9UPXt29Boiw1XnQOSNkpiGKF
        nBajzBAAAQDasZ4jXVhRTOsrvHQcOV0cJetfZRmmn2CSccSwEGFaQw3KtBxTZW5k
        ZXIgPHNlbmRlckBleGFtcGxlLnRlc3Q+iJAEExYKADgWIQSXozdGIGGewR0xZdRX
        JPmDasOD3QUCaVW5AAIbAwULCQgHAgYVCgkICwIEFgIDAQIeAQIXgAAKCRBXJPmD
        asOD3akuAPsHj1ZBuuvWP7N/2ul+/FY+Wrb5SYVWLxESwY1QXukRuwD8DLlVjusl
        /XPbQa45OyOFhnjkBKM7uo8TdU3ZhMfxPAWcXQRpVbkAEgorBgEEAZdVAQUBAQdA
        dKLItxuAQyd+hNUZ9iJFmDLzjQT9XEbkRu7ZTdgGrHkDAQgHAAD/RSg2nz//chZm
        gcLlN5yxhU5Q0Gg10ju08AqcF7QK2xgPyIh4BBgWCgAgFiEEl6M3RiBhnsEdMWXU
        VyT5g2rDg90FAmlVuQACGwwACgkQVyT5g2rDg914agEAqyLPYJf/resIqbXM5taH
        A7d1QSKKFF/K4NT341nALtUA/1if2zlVuHHTsZ81+DHkd8eUCV8lzVcnwUGOJcaS
        CykL
        =UxzN
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A key for <c>sender@example.test</c> whose primary key signs, protected with <see cref="OpenPgpPassphrase" />.</summary>
    internal const string OpenPgpProtected = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lIYEaVW5ABYJKwYBBAHaRw8BAQdAvxdd9ZKI5PHkfufiztcoZmBRfStpVxMAU07r
        nxLmlHL+BwMCll/mf/R8YmP/yY8D3F7QvLAqd+XO7UeVN4yeAApL9lBNJJLv7ypc
        qdjS1pJxv5UajN42y5j5zwTGpoJ4ntsIcYY2fR00zzRrf4yKsTaGwrQcU2VuZGVy
        IDxzZW5kZXJAZXhhbXBsZS50ZXN0PoiQBBMWCgA4FiEEpzizzKAlj3vaifQNfCqM
        1225W5IFAmlVuQACGwMFCwkIBwIGFQoJCAsCBBYCAwECHgECF4AACgkQfCqM1225
        W5JWXwD/Q72t0VvSVe06hdLnSb3V88Vek/V5sXmqXv12N9i5ms4BANSlD76pPWVa
        AA/0RBt4mlUJqHJEcPZQG1rBk5y78GAC
        =/TOG
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A key for <c>sender@example.test</c> whose primary key only certifies and whose subkey signs.</summary>
    internal const string OpenPgpSubkeySigner = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdAHRSa+/YGg/iB8GkeAX7G3HjjoYB5COYSPfkB
        u06je0IAAQCg+n8iyQD+Hml6MDuvrgJQjPNdQKyttTYRU2whI6I7Tg29tBxTZW5k
        ZXIgPHNlbmRlckBleGFtcGxlLnRlc3Q+iJAEExYKADgWIQSiz6uyfyxwfMxielBn
        rRm7q/SYuwUCaVW5AAIbAQULCQgHAgYVCgkICwIEFgIDAQIeAQIXgAAKCRBnrRm7
        q/SYu8dIAQCxtW30u4tCP26erxNrwKQxLclNH8SGZmsbfiA0ihpR3QD/cn4D2wAK
        QFnSmXW89WaWEoKTRfk4yIvRNkLPwU0GnQWcWARpVbkAFgkrBgEEAdpHDwEBB0Av
        BCxbQdySgf06AqU8cyj1bZR++SXMPbWyEuxKQGQAtgABAOdYXsS1mJN5VG0Bzys0
        iddQ09fkDW1HZJnkxnXlecWeEYeI7wQYFgoAIBYhBKLPq7J/LHB8zGJ6UGetGbur
        9Ji7BQJpVbkAAhsCAIEJEGetGbur9Ji7diAEGRYKAB0WIQSPyjd6HQe9Y0d4GqR/
        LEKFFulRCAUCaVW5AAAKCRB/LEKFFulRCOB3AQCdpbxi+cXR9msJrsGLCBucsJ79
        ZtY5v2M77v3+Vp4xbwEA57ckomYV+EJg/SzIKM+pOuUX8oXOB4FFYlg6EMk6nQaG
        VQD+NQnGmvmMmWMM5CQL667Y+jT1WjqnBpi/SKc1REXTzGEA/2ra2hmHH7cKS4cy
        FFB7Qo7aggJ8gzEu7U78Th8YD/EG
        =jjOk
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A key for <c>sender@example.test</c> whose primary key only certifies and whose subkey only encrypts.</summary>
    internal const string OpenPgpNoSigningKey = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdArEXjj07Rpn3CRsnQgcEIUNiuFSHGPP/EQUS7
        cB5e2CoAAQCB7T6x3h6vFqoD3X+htuq7MEvC01q0BPk2KZhypxC0xBDXtBxTZW5k
        ZXIgPHNlbmRlckBleGFtcGxlLnRlc3Q+iJAEExYKADgWIQSfwbMiqtEOfkikXUJE
        BD9DByuMZwUCaVW5AAIbAQULCQgHAgYVCgkICwIEFgIDAQIeAQIXgAAKCRBEBD9D
        ByuMZwYGAP9HDXxqKi5uhce/cqtBLP1SPOL9Zvf9NjxGWQMmJ3bVnQD/UgkUPjtO
        zZ7yYzPZuJGqRlfIl0m3t9g/CbSwv+3MDAecXQRpVbkAEgorBgEEAZdVAQUBAQdA
        vj+SlZ29zrMbPCEi+3LyFqRyVjJHKTJa9DzqTe0E2FwDAQgHAAD/Xuct0dyVUAcZ
        afA62x8aQTKtAqIUz1hOKBbPuxb60uAPoYh4BBgWCgAgFiEEn8GzIqrRDn5IpF1C
        RAQ/QwcrjGcFAmlVuQACGwwACgkQRAQ/QwcrjGdRQwEAvsK7IsMi6HdEhP6cd895
        Ome4c0vnF3ZoPSKniond0uEBALZaavvf3fXQtAm7pZJi8jsIU345gevUMLtEKQso
        VeoB
        =r/fb
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A signing key whose only user ID is <c>someone.else@example.test</c>.</summary>
    internal const string OpenPgpOtherUserId = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdAKNOBOR2OP4JqussRaBBY6FYIIerZcB847Gi8
        y8oLowoAAQCqOzr8nssS7oB26lYoeBps3u9XyYMnrnn0pPiGizBJIhDbtChTb21l
        b25lIEVsc2UgPHNvbWVvbmUuZWxzZUBleGFtcGxlLnRlc3Q+iJAEExYKADgWIQRz
        G6vglaaT3V1U89koIp3wbESXCwUCaVW5AAIbAwULCQgHAgYVCgkICwIEFgIDAQIe
        AQIXgAAKCRAoIp3wbESXC9UdAQDKK6JlmZ7i8qlh6rJRfYL+QscIOpPigfzXEUL0
        TfVYNgEAx8Mznn0LOSE3E3qbkxbPKoZ96MA55M0i3ZItkqtLPA0=
        =fXs2
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A signing key for <c>sender@example.test</c> created 2026-01-01 that expired on 2026-03-01.</summary>
    internal const string OpenPgpExpired = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdABhmVhKfEosPCH6gTfutSyMi5IrJJ/+zrztik
        8aMAY/wAAQD9badNw/b8p3QyiP8oKXftHTsrW3xzEXLTFUCDEV/nQA8vtBxTZW5k
        ZXIgPHNlbmRlckBleGFtcGxlLnRlc3Q+iJYEExYKAD4WIQRJmb+Kc1OeEIIKjPk1
        Mu5Y0k2UDQUCaVW5AAIbAwUJAE5jMAULCQgHAgYVCgkICwIEFgIDAQIeAQIXgAAK
        CRA1Mu5Y0k2UDZHlAQCct+QrZh5Yv0kCL6VxYe8qFCvQ/beRI/KnEeMdYR1W1QEA
        x6sf3wP1+5p/XtikFYfRUp9SYk6L/Tw2fXppew4TPQg=
        =IFyk
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A signing key for <c>sender@example.test</c> carrying its own revocation.</summary>
    internal const string OpenPgpRevoked = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdAJkV7mljNKlpXkRnGR6KCm9s3TWiSSLF5x2VU
        7Fv3LTsAAP0Qq4z24YO9TfW5M2sNMMTaWr9jBoq6BTBbuBjgtI3BphF9iHgEIBYK
        ACAWIQT+A6Ym+qfelqZfjMaIixviFlN8qAUCaVW5AAIdAAAKCRCIixviFlN8qO5E
        AQDwlF7mmxcja49+UhpjkF+DiHluM6TSQXLmM1fi+imY2AD/bNG8ow3cvjlVbb0Q
        uIT1EHRjnBae0CclB00FdyEwNgm0HFNlbmRlciA8c2VuZGVyQGV4YW1wbGUudGVz
        dD6IkAQTFgoAOBYhBP4Dpib6p96Wpl+MxoiLG+IWU3yoBQJpVbkAAhsDBQsJCAcC
        BhUKCQgLAgQWAgMBAh4BAheAAAoJEIiLG+IWU3yogJUBAMVoJmm++lNyNQ+Vdt5P
        16dGVBJoC7yeORrtpv5zjrqgAQCK+zq6cCT5so9F6x0ykS0H7KglYRWleyqbGoWG
        TrpTBw==
        =Itm8
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A key for <c>sender@example.test</c> whose primary key only certifies and whose one signing subkey was revoked on 2026-01-02.</summary>
    internal const string OpenPgpRevokedSigningSubkey = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdAUVnCJUZVP5seprjejz+lULxNyoQ+Ubt5Wnt0
        6UGoGYsAAQDkyoMtLAO8sGQl15UYd8FXEMecr13iulESPRCyqrT/mRAJtBxTZW5k
        ZXIgPHNlbmRlckBleGFtcGxlLnRlc3Q+iJAEExYKADgWIQTYT0O1/G5i01w6cjN1
        CUCcrXgurQUCaVW5AAIbAQULCQgHAgYVCgkICwIEFgIDAQIeAQIXgAAKCRB1CUCc
        rXgurbhRAQDW1gj0wszPu/jNpVqCPM+SCp71/WCgjOPsjrAoh+ximAEAzaPJLEGl
        /ya3e2g0yM02+4G6R4PiE0wqSnZvPNtexAicWARpVbkAFgkrBgEEAdpHDwEBB0Dw
        cZzDAbg/5pWXFZfdqQ5wG3Bv5uLz8mVnWxdo2W7KIAAA/1ZK1Z6tgFBG/fh/AnmG
        srbkIqVVxlCbv/6iN71MQAYqEXKIeAQoFgoAIBYhBNhPQ7X8bmLTXDpyM3UJQJyt
        eC6tBQJpVwqAAh0AAAoJEHUJQJyteC6t0BEA/j7XSzKG2DVQTtG9QppRhFCPeY6C
        btBrTwl1KqCqiHYQAQCyBg0Qx3hDNdayUNyPKx2nEITjQDf6UinfqXmSNgWZA4jv
        BBgWCgAgFiEE2E9DtfxuYtNcOnIzdQlAnK14Lq0FAmlVuQACGwIAgQkQdQlAnK14
        Lq12IAQZFgoAHRYhBAaG11tJqc9LNutRf5yRsnbIqwxOBQJpVbkAAAoJEJyRsnbI
        qwxOYGIA/3yB6cQhmm2bZOLMeZci3MQEHhBVOc9yRKB74hVWjnxbAP4+FaZpb9bJ
        3pZhpHrZiGtHb0P86H5SsA6JQgMML8HrBZXIAPwNSPmzTIoonS0ZJrKC4I/jXQq9
        NFLzFmlCRkfguZcUvwD+J+hDEdVOBB310X0wTkPyLBjpPERurVlwXTqFVFfmxAw=
        =w3oo
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A key for <c>sender@example.test</c> whose primary key only certifies and whose one signing subkey expired on 2026-03-01.</summary>
    internal const string OpenPgpExpiredSigningSubkey = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdA/TC6jLwgCI7jqw1SdNV6xTS6BbTC+dSuJzh5
        gt287TAAAQDqErnUSbTIPDVtldhXnIGtI0X/1ROzWOrzQ4twWqhYjBEWtBxTZW5k
        ZXIgPHNlbmRlckBleGFtcGxlLnRlc3Q+iJAEExYKADgWIQTAvg/1W7J6MZ3UEB95
        mtuM+r9EXQUCaVW5AAIbAQULCQgHAgYVCgkICwIEFgIDAQIeAQIXgAAKCRB5mtuM
        +r9EXedjAP9xRyXRrudmAS+RnCe7oSiPL1vGyPhFWnJd5tQWJ7yQawD8C9nHtbff
        tg4BojmgLhSjaYSVAU0EUk+dz/h98wJgOAycWARpVbkAFgkrBgEEAdpHDwEBB0BK
        vSGVyLFg/GtMw6RRONjN3DJk/oKd7ucvNGGaRcuzMQAA/i/e8COUaA0Vg6XQKGC3
        3jy5DmgG2UDhcXY26MIhnBvuEEmI9QQYFgoAJhYhBMC+D/VbsnoxndQQH3ma24z6
        v0RdBQJpVbkAAhsCBQkATmMwAIEJEHma24z6v0RddiAEGRYKAB0WIQQbrVF+j550
        JFYAKHBVqlHi9DzNiAUCaVW5AAAKCRBVqlHi9DzNiOdSAQDy4iQnU9qbc9rOnpIr
        AZvdpfV6NYUsN2mLWuDXYiwNxQEAwZ2aBQLcBQGYur4ZY0xqeGGiMDUs8Ghs/uwn
        NcEB5Q07mAD/VpWvoGaLj9mPjX58mcsKXQF9ZSnEFph02EK2dCc24VUBALCR21S8
        znDiBSQFpJhZiAQiwRL5mYqKkqvFAiPHk7AM
        =aOgh
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A signing key whose user ID for <c>sender@example.test</c> was revoked on 2026-01-02, leaving only <c>someone.else@example.test</c>.</summary>
    internal const string OpenPgpRevokedUserId = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdAqmG/wZakG/RVDmML1D5nv6JeLbmi0x0eVwbV
        0ZLbZoUAAQDtuhXA04tXPcI8T45Ut94XoLrufVMLeZp3ELkUoO5RpRBYtBxTZW5k
        ZXIgPHNlbmRlckBleGFtcGxlLnRlc3Q+iHgEMBYKACAWIQQjs/CvSUZuts2yLN3l
        /BQUQelQoAUCaVcKgAIdIAAKCRDl/BQUQelQoPDeAP4/TrJ38lcJNwNiEFmiTgNn
        3shoL9p43YNRg6Tdjjxc8AEAnEM/kDhzVzYJ16qCibmpuTxIwWB8nC92EjR3mmGq
        GAWIkAQTFgoAOBYhBCOz8K9JRm62zbIs3eX8FBRB6VCgBQJpVbkAAhsDBQsJCAcC
        BhUKCQgLAgQWAgMBAh4BAheAAAoJEOX8FBRB6VCg09MBANIj20C/MzplchihGsP8
        lVbng8oxUaSZhh1vmYRUCRmPAPwNKRMYAb109mkITeahBKHnLIJedKPbZlic+IHX
        CQHGB7QoU29tZW9uZSBFbHNlIDxzb21lb25lLmVsc2VAZXhhbXBsZS50ZXN0PoiQ
        BBMWCgA4FiEEI7Pwr0lGbrbNsizd5fwUFEHpUKAFAmlVuQACGwMFCwkIBwIGFQoJ
        CAsCBBYCAwECHgECF4AACgkQ5fwUFEHpUKDZgwD/bBD/nWtU9Op4hhIrRm9bBH/f
        DyANWp4G3PyneQPkTEUBALKxeQCA/U0i9O7I/FkDpSzlLPs81icxV54F212SJPMF
        =k+0h
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>A signing key whose user ID for <c>sender@example.test</c> was revoked on 2026-01-02 and certified again on 2026-01-03, the certification made with BouncyCastle because GnuPG will not add a user ID the key already holds.</summary>
    internal const string OpenPgpRecertifiedUserId = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdAjDOMj+8nReJhb2RVIGU056AUCZibf7okVhwQ
        +xjTyRsAAQDhlGHnUlbprc+QOQgVJSq9aEa/z00dYl/R/jwsK7QuLA6UtBxTZW5k
        ZXIgPHNlbmRlckBleGFtcGxlLnRlc3Q+iHgEMBYKACAWIQQRsXlK0Lh0EiifL7XX
        fz5lri7l+AUCaVcKgAIdIAAKCRDXfz5lri7l+GsWAQCL0t5i7JhnlLuNe1FjxGn5
        9bpu6lfGSEVr/INgk1SV9wD+IHZPpz9P69rr0v26IbFShUHQRgxNbk01YTWaHZht
        4wmIkAQTFgoAOBYhBBGxeUrQuHQSKJ8vtdd/PmWuLuX4BQJpVbkAAhsDBQsJCAcC
        BhUKCQgLAgQWAgMBAh4BAheAAAoJENd/PmWuLuX4Xm8BAOXvm67dI3Zsq09jS9SH
        2+8CR1QRTgMWU5Wk1ieTnPgiAQCGf/HCzLsiY0lUVGuZSAGlyzm+GcmKeTB1nPqj
        qLJnCYh4BBMWCAAgBQJpWFwAFiEEEbF5StC4dBIony+1138+Za4u5fgCGwMACgkQ
        138+Za4u5fgaGQEA4jgMAyxKFCE973iIi6iTgpy8QLBghDrWQaku0dr3XP0A/i7J
        gDOnEoDsCURZraaKl0/WFkIMq365JlWEKlygzywPtChTb21lb25lIEVsc2UgPHNv
        bWVvbmUuZWxzZUBleGFtcGxlLnRlc3Q+iJAEExYKADgWIQQRsXlK0Lh0EiifL7XX
        fz5lri7l+AUCaVW5AAIbAwULCQgHAgYVCgkICwIEFgIDAQIeAQIXgAAKCRDXfz5l
        ri7l+K8gAP9uKxcb3+4V7RqA4TfT/aNeDc6d25VakOgU+ujXkEdT3wEA5Zrp6fq3
        QixJTjVTLdKDUxHdNzBPp9d7W+Nf5mCexwE=
        =vBkV
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>Two signing keys for <c>sender@example.test</c> in one armored block.</summary>
    internal const string OpenPgpTwoKeys = """
        -----BEGIN PGP PRIVATE KEY BLOCK-----

        lFgEaVW5ABYJKwYBBAHaRw8BAQdAbPIBZwK/Ep240gL/ffd+EgQWe8bh7Z4hwKi3
        Ml8RWBAAAQD8z6LgyjP2vlcAaClO0AMLTLcSd8fd/ZVpiFcy3pLIExCatBxTZW5k
        ZXIgPHNlbmRlckBleGFtcGxlLnRlc3Q+iJAEExYKADgWIQTgd3I77QQnJac3VLNg
        1RmBM8hEfAUCaVW5AAIbAwULCQgHAgYVCgkICwIEFgIDAQIeAQIXgAAKCRBg1RmB
        M8hEfJBXAQDlsCSC7AxTqTk4Q/IRaWInP4TB9GD11EWw3vsz1kgM9AD/ZwlbGd3e
        fMGaHe7uClySMvSGdwei8aBgeE7INuiUrgSUWARpVbkAFgkrBgEEAdpHDwEBB0A6
        Fc1iFG6ppsGPk/yxZX0PnUHJcWz1lZ6CqxXf9aq41QAA/0pC0U7F38dkm1DZ2VOF
        NMRvGJoYuHXA/agSS54/PP1OEXK0IlNlbmRlciBBZ2FpbiA8c2VuZGVyQGV4YW1w
        bGUudGVzdD6IkAQTFgoAOBYhBLixPgijIDAbCuQb28TvjtdBGTQpBQJpVbkAAhsD
        BQsJCAcCBhUKCQgLAgQWAgMBAh4BAheAAAoJEMTvjtdBGTQpHzoA/3QBgFg9y0rK
        WUBDlbm316/7d3N2/0eN0MHCxY44Wp1YAQCboMYbAtR4y8TEk5GhVhr8tkzff7NR
        Z+9Fai8xGEybBQ==
        =YnMi
        -----END PGP PRIVATE KEY BLOCK-----
        """;

    /// <summary>The public half of <see cref="OpenPgpSigner" />, which holds nothing to sign with.</summary>
    internal const string OpenPgpPublicKey = """
        -----BEGIN PGP PUBLIC KEY BLOCK-----

        mDMEaVW5ABYJKwYBBAHaRw8BAQdA1st8ns5gRnDi9UPXt29Boiw1XnQOSNkpiGKF
        nBajzBC0HFNlbmRlciA8c2VuZGVyQGV4YW1wbGUudGVzdD6IkAQTFgoAOBYhBJej
        N0YgYZ7BHTFl1Fck+YNqw4PdBQJpVbkAAhsDBQsJCAcCBhUKCQgLAgQWAgMBAh4B
        AheAAAoJEFck+YNqw4PdqS4A+wePVkG669Y/s3/a6X78Vj5atvlJhVYvERLBjVBe
        6RG7APwMuVWO6yX9c9tBrjk7I4WGeOQEozu6jxN1TdmEx/E8Bbg4BGlVuQASCisG
        AQQBl1UBBQEBB0B0osi3G4BDJ36E1Rn2IkWYMvONBP1cRuRG7tlN2AaseQMBCAeI
        eAQYFgoAIBYhBJejN0YgYZ7BHTFl1Fck+YNqw4PdBQJpVbkAAhsMAAoJEFck+YNq
        w4PdeGoBAKsiz2CX/63rCKm1zObWhwO3dUEiihRfyuDU9+NZwC7VAP9Yn9s5Vbhx
        07GfNfgx5HfHlAlfJc1XJ8FBjiXGkgspCw==
        =szpP
        -----END PGP PUBLIC KEY BLOCK-----
        """;

    /// <summary>The passphrase <see cref="OpenPgpProtected" /> is protected with.</summary>
    internal const string OpenPgpPassphrase = "signing-passphrase";
}
