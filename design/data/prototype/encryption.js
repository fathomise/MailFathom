// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

/* ENCRYPTED MAIL — what the service publishes about encryption (ADR 0046), read by `protectionOf` and by the thread's
   reading. A received message carries `encrypted` in `threads.js`: `opened` where a key MailFathom holds for its
   account opened it, `noKey` where none does, `limit` where opening it went past a limit every message is held to.
   `accounts` says what each mail account can do: `encrypt` where MailFathom holds a key to encrypt with for it, `sign`
   where it holds one to sign with. `recipients` names who MailFathom holds a key for, by the name a chip shows, and
   where the key came from; anybody absent has none. */
MailFathomDesign.data.encryption = {
  accounts: {
    acc1: { encrypt: true, sign: true },
    acc2: { encrypt: false, sign: false },
    acc3: { encrypt: true, sign: false },
  },
  recipients: {
    "Anna Kowalska": "configured",
    "Ewa Sikora": "learned",
    "Piotr Zieliński": "configured",
    "Robert Lis": "configured",
    "Tomasz Bąk": "learned",
  },
};
