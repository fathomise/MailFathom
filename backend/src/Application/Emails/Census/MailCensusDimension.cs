// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Census;

/// <summary>What a census of the mailbox groups the mail it counts by.</summary>
public enum MailCensusDimension
{
    /// <summary>No grouping: one total for the scope and the filters.</summary>
    Total = 0,

    /// <summary>The sender's address, compared in its normalized form.</summary>
    SenderAddress = 1,

    /// <summary>The domain of the sender's address, which is the organisation a message came from as far as its address says.</summary>
    SenderDomain = 2,

    /// <summary>The day a message arrived on, on the person's clock.</summary>
    Day = 3,

    /// <summary>The week a message arrived in, starting on Monday, on the person's clock.</summary>
    Week = 4,

    /// <summary>The month a message arrived in, on the person's clock.</summary>
    Month = 5,

    /// <summary>The categories a message was sorted into that its account shows, a message in several counting in each.</summary>
    Category = 6,
}
