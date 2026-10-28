// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Trust;

/// <summary>Which of the books a receiving account reaches holds a contact.</summary>
public enum SenderTrustBook
{
    /// <summary>The book of the organization the receiving account belongs to.</summary>
    Organization = 0,

    /// <summary>The own book of a user the receiving account is assigned to.</summary>
    Personal = 1,
}
