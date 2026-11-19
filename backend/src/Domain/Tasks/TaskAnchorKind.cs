// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Tasks;

/// <summary>Names what a <see cref="TaskAnchor" /> points at.</summary>
public enum TaskAnchorKind
{
    /// <summary>A mail conversation.</summary>
    Thread = 0,

    /// <summary>A calendar event.</summary>
    Event = 1,
}
