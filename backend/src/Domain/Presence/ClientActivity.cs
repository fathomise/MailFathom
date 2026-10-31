// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Presence;

/// <summary>Whether a person is using one open client, as that client measures it from its own input.</summary>
public enum ClientActivity
{
    /// <summary>The client received input within the last ten minutes.</summary>
    Active = 0,

    /// <summary>The client has received no input for ten minutes.</summary>
    Idle = 1,
}
