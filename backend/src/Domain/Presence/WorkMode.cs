// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Presence;

/// <summary>Where a member says they work from.</summary>
public enum WorkMode
{
    /// <summary>From the office.</summary>
    Office = 0,

    /// <summary>From anywhere else.</summary>
    Remote = 1,
}
