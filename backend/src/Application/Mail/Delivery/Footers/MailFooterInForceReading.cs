// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>The footer a message sent now from one account would carry, as a reader is shown it.</summary>
/// <param name="Account">The account.</param>
/// <param name="Source">Whether it is the reader's own footer or the organization's.</param>
/// <param name="Document">The footer reduced to the closed document tree a client draws, its images inlined, or <see langword="null" /> where it reduced to nothing.</param>
public sealed record MailFooterInForceReading(MailAccountId Account, MailFooterSource Source, MailDocument? Document);
