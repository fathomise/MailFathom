// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>Which of a stored message's flags hold a value of MailFathom's own that the next first observation has to leave standing.</summary>
/// <remarks>Either an act wrote the field before any window had read it, or the message was read once already and has since been carried to a new occurrence.</remarks>
[Flags]
internal enum EmailFieldsSetBeforeFirstObservation
{
    /// <summary>None of them does.</summary>
    None = 0,

    /// <summary>The read state.</summary>
    Seen = 1,

    /// <summary>The star.</summary>
    Flagged = 2,

    /// <summary>The keywords.</summary>
    Keywords = 4,
}
