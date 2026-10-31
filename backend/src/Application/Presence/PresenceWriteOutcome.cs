// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Presence;

/// <summary>What became of a write to a member's own presence.</summary>
public enum PresenceWriteOutcome
{
    /// <summary>The write was recorded.</summary>
    Recorded = 0,

    /// <summary>The caller belongs to no organization, so nobody could see their presence and nothing was recorded.</summary>
    NotInOrganization = 1,

    /// <summary>The client is new and the member already keeps the most clients open, so nothing was recorded.</summary>
    TooManyClients = 2,
}
