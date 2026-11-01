// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Where the footer a message carries comes from.</summary>
public enum MailFooterSource
{
    /// <summary>The footer the sending user wrote for the account.</summary>
    Own = 0,

    /// <summary>The template of the organization holding the account, filled from the sending user's record.</summary>
    Organization = 1,
}
