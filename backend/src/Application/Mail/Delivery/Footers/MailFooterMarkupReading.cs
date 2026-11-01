// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>A footer's markup once it was read: sanitized, or refused.</summary>
/// <param name="Html">The sanitized markup, or <see langword="null" /> when it was refused.</param>
/// <param name="Refusal">Why it was refused, or <see langword="null" /> when it was not.</param>
public sealed record MailFooterMarkupReading(string? Html, MailFooterRefusal? Refusal);
