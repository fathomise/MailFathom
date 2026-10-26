// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What the service publishes about who sent a received message, keyed by thread: the newest message's facts, which an
// earlier message from the same sender shares. `checker` is who checked sender authentication - the account's trusted
// receiving server, MailFathom's own DKIM check, or nobody; `dkim` and `spf` name the domain whose check passed, null
// where none did; `dmarc` is pass, fail, none (no policy published), unreported, temperror or permerror; `author` is
// whether the domain in From was established as the author and `authDomain` the domain that actually authenticated
// it; `trust` says why this account recognizes the author; `sig` is the S/MIME or OpenPGP signature, null where the
// message carries none. A thread missing here is an authenticated sender the account does not recognize.
MailFathomDesign.data.senderMarks = {
  servers: { acc1: "mx.nordwind.example" },
  ownDomains: { acc1: "nordwind.example", acc2: "mail.example" },
  threads: {
    contoso: {
      from: "a.kowalska@contoso.example", checker: "server", dkim: "contoso.example", spf: "contoso.example", dmarc: "pass",
      author: "authenticated", authDomain: "contoso.example", trust: { source: "list", match: "contoso.example" },
      sig: {
        format: "smime-detached", signedAt: "27.08.2026 09:13", content: "pass", signer: "a.kowalska@contoso.example",
        ca: { result: "public", name: "Harbourline Public Email CA R4" }, validity: "pass",
        revocation: { result: "pass", how: "OCSP", at: "27.08.2026 09:14" },
        cert: {
          subject: "CN=Anna Kowalska, O=Contoso", email: "a.kowalska@contoso.example", issuer: "Harbourline Public Email CA R4",
          valid: "12.01.2026 - 11.01.2028", serial: "4f 1c 9a 02 7e d3 55 18", fingerprint: "SHA-256 9C:41:E7:0B:22:5A:D8:13:6F:A0:3E:91:B4:7C:58:2D",
          algorithms: "RSA 3072 · SHA-256",
        },
      },
    },
    anna2: {
      from: "a.kowalska@contoso.example", checker: "server", dkim: "contoso.example", spf: "contoso.example", dmarc: "pass",
      author: "authenticated", authDomain: "contoso.example", trust: { source: "list", match: "contoso.example" }, sig: null,
    },
    finanse: {
      from: "invoices@company.example", checker: "server", dkim: "company.example", spf: "company.example", dmarc: "pass",
      author: "authenticated", authDomain: "company.example", trust: { source: "list", match: "invoices@company.example" },
      sig: {
        format: "smime-opaque", smimeType: "signed-data", signedAt: "27.08.2026 08:01", content: "fail", signer: "invoices@company.example",
        ca: { result: "public", name: "Harbourline Public Email CA R4" }, validity: "pass",
        revocation: { result: "pass", how: "OCSP", at: "27.08.2026 08:03" },
        cert: {
          subject: "CN=Invoices, O=Company", email: "invoices@company.example", issuer: "Harbourline Public Email CA R4",
          valid: "03.03.2026 - 02.03.2027", serial: "0a 77 e2 41 9c 10 3b d6", fingerprint: "SHA-256 1E:88:4D:F0:7A:C3:29:B5:60:0D:E2:47:9B:13:CA:74",
          algorithms: "ECDSA P-256 · SHA-256",
        },
      },
    },
    piotr: {
      from: "p.zielinski@company.example", checker: "server", dkim: null, spf: "lists.partners.example", dmarc: "fail",
      author: "failed", authDomain: "lists.partners.example", trust: null,
      sig: {
        format: "smime-detached", signedAt: "26.08.2026 16:39", content: "pass", signer: "p.zielinski@company.example",
        ca: { result: "public", name: "Harbourline Public Email CA R4" }, validity: "pass",
        revocation: { result: "pass", how: "OCSP", at: "26.08.2026 16:42" },
        cert: {
          subject: "CN=Piotr Zieliński, O=Company", email: "p.zielinski@company.example", issuer: "Harbourline Public Email CA R4",
          valid: "20.05.2026 - 19.05.2028", serial: "62 d0 1f 8b 33 a4 07 c9", fingerprint: "SHA-256 B7:05:3C:9E:41:D2:88:6A:F1:27:0C:E5:93:4B:7D:12",
          algorithms: "RSA 3072 · SHA-256",
        },
      },
    },
    marta: {
      from: "m.nowak@company.example", checker: "server", dkim: "company.example", spf: "company.example", dmarc: "pass",
      author: "authenticated", authDomain: "company.example", trust: null,
      sig: {
        format: "smime-detached", signedAt: "26.08.2026 11:18", content: "pass", signer: "m.nowak@company.example",
        ca: { result: "public", name: "Harbourline Public Email CA R4" }, validity: "pass",
        revocation: { result: "revoked", how: "OCSP", at: "26.08.2026 11:21", on: "14.08.2026" },
        cert: {
          subject: "CN=Marta Nowak, O=Company", email: "m.nowak@company.example", issuer: "Harbourline Public Email CA R4",
          valid: "02.02.2026 - 01.02.2028", serial: "19 e4 b0 5a 6c 22 f1 8e", fingerprint: "SHA-256 44:9A:0E:D7:13:6B:C5:F2:8D:31:A9:07:E6:5C:B0:3F",
          algorithms: "RSA 2048 · SHA-256",
        },
      },
    },
    jacek: {
      from: "j.wrona@lawoffice.example", checker: "server", dkim: "lawoffice.example", spf: "lawoffice.example", dmarc: "none",
      author: "authenticated", authDomain: "lawoffice.example", trust: null, sig: null,
    },
    prawnik2: {
      from: "j.wrona@lawoffice.example", checker: "server", dkim: "lawoffice.example", spf: "lawoffice.example", dmarc: "none",
      author: "authenticated", authDomain: "lawoffice.example", trust: null,
      sig: {
        format: "smime-detached", signedAt: "26.08.2026 15:38", content: "pass", signer: "j.wrona@lawoffice.example",
        ca: { result: "none", name: "Wrona Law Office Internal CA" }, validity: "pass", revocation: { result: "skipped" },
        cert: {
          subject: "CN=Jacek Wrona, O=Wrona Law Office", email: "j.wrona@lawoffice.example", issuer: "Wrona Law Office Internal CA",
          valid: "01.07.2025 - 30.06.2027", serial: "01 00 2c", fingerprint: "SHA-256 7F:22:B1:C8:05:E9:3D:74:AA:16:5B:0F:C2:98:61:E3",
          algorithms: "RSA 2048 · SHA-256",
        },
      },
    },
    hr1: {
      from: "hr@company.example", checker: "server", dkim: "company.example", spf: "company.example", dmarc: "pass",
      author: "authenticated", authDomain: "company.example", trust: null,
      sig: {
        format: "smime-opaque", smimeType: "signed-data", signedAt: "27.08.2026 07:54", content: "pass", signer: "hr-system@company.example",
        ca: { result: "public", name: "Harbourline Public Email CA R4" }, validity: "pass",
        revocation: { result: "pass", how: "OCSP", at: "27.08.2026 07:56" },
        cert: {
          subject: "CN=HR System, O=Company", email: "hr-system@company.example", issuer: "Harbourline Public Email CA R4",
          valid: "10.04.2026 - 09.04.2027", serial: "3b 9f 61 c0 de 04 72 15", fingerprint: "SHA-256 D0:6E:17:4A:B9:3C:52:E8:0F:91:7D:A6:24:C3:5B:88",
          algorithms: "ECDSA P-256 · SHA-256",
        },
      },
    },
    north1: {
      from: "r.lis@northwind.example", checker: "server", dkim: "northwind.example", spf: null, dmarc: "temperror",
      author: "notEstablished", authDomain: null, trust: null,
      sig: {
        format: "pgp", signedAt: "27.08.2026 07:10", content: "pass", signer: "r.lis@northwind.example",
        key: "known", keyValidity: "expired", keyExpired: "30.06.2026", revocation: { result: "pass" },
        pgpKey: {
          fingerprint: "3A1F 9C40 E27B 55D1 0C8E  44B2 91F7 6A03 D5E8 2C19", keyId: "91F76A03D5E82C19", userId: "Robert Lis <r.lis@northwind.example>",
          created: "01.07.2024", expires: "30.06.2026", algorithms: "Ed25519 · SHA-256",
        },
      },
    },
    bank1: {
      from: "noreply@bank.example", checker: "server", dkim: "bulk-sender.example", spf: "bulk-sender.example", dmarc: "fail",
      author: "failed", authDomain: "bulk-sender.example", trust: null, sig: null,
    },
    fabrikam1: {
      from: "t.bak@fabrikam.example", checker: "server", dkim: "fabrikam.example", spf: "fabrikam.example", dmarc: "pass",
      author: "authenticated", authDomain: "fabrikam.example", trust: null,
      sig: {
        format: "smime-detached", signedAt: "26.08.2026 17:03", content: "pass", signer: "t.bak@fabrikam.example",
        ca: { result: "public", name: "Harbourline Public Email CA R4" }, validity: "expired", expired: "31.07.2026",
        revocation: { result: "pass", how: "CRL", at: "26.08.2026 17:06" },
        cert: {
          subject: "CN=Tomasz Bąk, O=Fabrikam", email: "t.bak@fabrikam.example", issuer: "Harbourline Public Email CA R4",
          valid: "01.08.2024 - 31.07.2026", serial: "5d 08 a3 97 21 6e c4 0b", fingerprint: "SHA-256 2B:C9:70:E4:5F:18:A6:3D:91:0E:7C:B2:46:D5:08:F1",
          algorithms: "RSA 2048 · SHA-256",
        },
      },
    },
    audyt1: {
      from: "audit@company.example", checker: "server", dkim: "company.example", spf: "company.example", dmarc: "pass",
      author: "authenticated", authDomain: "company.example", trust: null,
      sig: {
        format: "smime-detached", signedAt: "26.08.2026 16:10", content: "pass", signer: "audit@company.example",
        ca: { result: "operator", name: "Company Internal Root CA" }, validity: "purpose",
        revocation: { result: "pass", how: "CRL", at: "26.08.2026 16:13" },
        cert: {
          subject: "CN=Internal Audit, O=Company", email: "audit@company.example", issuer: "Company Internal Root CA",
          valid: "15.01.2026 - 14.01.2028", serial: "72 4a 0c e9 18 b5 33 d7", fingerprint: "SHA-256 6C:D3:28:9B:E0:47:15:AF:72:C8:3E:06:B9:51:D4:2A",
          algorithms: "RSA 3072 · SHA-256",
        },
      },
    },
    travel1: {
      from: "bookings@travel.example", checker: "own", dkim: "travel.example", spf: null, dmarc: "unreported",
      author: "authenticated", authDomain: "travel.example", trust: null,
      sig: {
        format: "smime-detached", signedAt: "26.08.2026 14:00", content: "pass", signer: "bookings@travel.example",
        ca: { result: "public", name: "Harbourline Public Email CA R4" }, validity: "pass", revocation: { result: "unchecked" },
        cert: {
          subject: "CN=Travel Desk, O=Travel", email: "bookings@travel.example", issuer: "Harbourline Public Email CA R4",
          valid: "09.09.2025 - 08.09.2027", serial: "28 c1 77 0d f4 9a 6b 30", fingerprint: "SHA-256 E5:1A:7D:30:C6:92:4F:0B:88:E3:15:D9:2C:6A:B7:40",
          algorithms: "ECDSA P-384 · SHA-384",
        },
      },
    },
    marketing1: {
      from: "marketing@company.example", checker: "server", dkim: "company.example", spf: "company.example", dmarc: "pass",
      author: "authenticated", authDomain: "company.example", trust: null,
      sig: {
        format: "pgp", signedAt: "26.08.2026 12:28", content: "unchecked", signer: null, key: "unknown",
        pgpKey: { keyId: "C4D29E11A07B6F53" },
      },
    },
    klient3: {
      from: "e.sikora@adventure.example", checker: "server", dkim: "adventure.example", spf: "adventure.example", dmarc: "pass",
      author: "authenticated", authDomain: "adventure.example", trust: null,
      sig: {
        format: "pgp", signedAt: "26.08.2026 10:13", content: "pass", signer: "e.sikora@adventure.example",
        key: "known", keyValidity: "pass", revocation: { result: "pass" },
        pgpKey: {
          fingerprint: "8E02 41BD 7C3F 9A16 E5D0  2B87 4C19 F3A2 06E8 D7B1", keyId: "4C19F3A206E8D7B1", userId: "Ewa Sikora <e.sikora@adventure.example>",
          created: "14.03.2025", expires: "13.03.2028", algorithms: "Ed25519 · SHA-512",
        },
      },
    },
    devops1: {
      from: "ci@nordwind.example", checker: "server", dkim: "nordwind.example", spf: "nordwind.example", dmarc: "pass",
      author: "authenticated", authDomain: "nordwind.example", trust: { source: "own", match: "nordwind.example" },
      sig: {
        format: "smime-detached", signedAt: "27.08.2026 06:19", content: "pass", signer: "ci@nordwind.example",
        ca: { result: "operator", name: "Nordwind Internal Issuing CA" }, validity: "pass",
        revocation: { result: "pass", how: "CRL", at: "27.08.2026 06:00" },
        cert: {
          subject: "CN=Nordwind CI, O=Nordwind", email: "ci@nordwind.example", issuer: "Nordwind Internal Issuing CA",
          valid: "01.06.2026 - 31.05.2027", serial: "0e 5b 99 c2 40 7d 18 a6", fingerprint: "SHA-256 A3:7E:12:C0:6D:49:F8:25:0B:E1:93:5C:2F:D6:84:1B",
          algorithms: "ECDSA P-256 · SHA-256",
        },
      },
    },
    rekrutacja1: {
      from: "a.bielska@company.example", checker: "server", dkim: "company.example", spf: "company.example", dmarc: "pass",
      author: "authenticated", authDomain: "company.example", trust: { source: "list", match: "a.bielska@company.example" }, sig: null,
    },
    spamPayroll: {
      from: "payroll@nordwlnd-hr.example", checker: "server", dkim: "nordwlnd-hr.example", spf: "nordwlnd-hr.example", dmarc: "pass",
      author: "authenticated", authDomain: "nordwlnd-hr.example", trust: null, sig: null,
    },
    spamChairs: {
      from: "offers@brightline-deals.example", checker: "own", dkim: "brightline-deals.example", spf: null, dmarc: "unreported",
      author: "authenticated", authDomain: "brightline-deals.example", trust: null, sig: null,
    },
    spamDesk: {
      from: "it-desk@nordwind-support.example", checker: "server", dkim: "mailer-cloud.example", spf: "mailer-cloud.example", dmarc: "fail",
      author: "failed", authDomain: "mailer-cloud.example", trust: null, sig: null,
    },
    spamSupplier: {
      from: "accounts@halvard-logistlcs.example", checker: "server", dkim: null, spf: "halvard-logistlcs.example", dmarc: "permerror",
      author: "notEstablished", authDomain: null, trust: null, sig: null,
    },
    spamParcel: {
      from: "notice@parcel-track.example", checker: "server", dkim: "parcel-track.example", spf: "parcel-track.example", dmarc: "none",
      author: "authenticated", authDomain: "parcel-track.example", trust: null, sig: null,
    },
    spamEstate: {
      from: "executor@marlowe-estate.example", checker: "none", dkim: null, spf: null, dmarc: "unreported",
      author: "notEstablished", authDomain: null, trust: null, sig: null,
    },
    spamThreat: {
      from: "noreply@mail-relay.example", checker: "server", dkim: null, spf: null, dmarc: "fail",
      author: "failed", authDomain: null, trust: null, sig: null,
    },
    spamSignals: {
      from: "picks@weekly-signals.example", checker: "none", dkim: null, spf: null, dmarc: "unreported",
      author: "notEstablished", authDomain: null, trust: null, sig: null,
    },
  },
};
