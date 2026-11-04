// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Mutations.Filing;

/// <summary>How far the append half of a message filed into another account has got.</summary>
public enum CrossAccountFilingStage
{
    /// <summary>Written down in the commit that filed the message, and not yet asked of the destination's server.</summary>
    Recorded = 0,

    /// <summary>The append has been issued, and no answer has been written down yet.</summary>
    AppendIssued = 1,

    /// <summary>The destination's server holds the message, and its copy is bound where the server named it.</summary>
    Appended = 2,

    /// <summary>The append was given up, and the whole act was reverted with it.</summary>
    Abandoned = 3,
}
