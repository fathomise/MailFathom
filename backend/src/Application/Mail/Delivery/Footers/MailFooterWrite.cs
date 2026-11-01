// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>What became of writing a footer or a template.</summary>
/// <param name="Outcome">Whether it was saved, and why not where it was not.</param>
/// <param name="Saved">The footer as it was saved, sanitized, where it was.</param>
/// <param name="Refusal">What was wrong with what was written, where <paramref name="Outcome" /> is <see cref="MailFooterWriteOutcome.Refused" />.</param>
public sealed record MailFooterWrite(
    MailFooterWriteOutcome Outcome,
    MailFooterContent? Saved = null,
    MailFooterRefusal? Refusal = null);
