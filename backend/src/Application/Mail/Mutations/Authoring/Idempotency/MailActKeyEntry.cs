// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Mutations.Authoring.Idempotency;

/// <summary>What the store holds for one person's key on one message.</summary>
/// <param name="Fingerprint">The digest of what the whole request asked.</param>
/// <param name="Outcome">What the first attempt answered for the message.</param>
public sealed record MailActKeyEntry(string Fingerprint, MailActOutcome Outcome);
