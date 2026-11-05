// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Mutations.Filing;

/// <summary>Why a message may not be filed into a folder of another account, each a sentence a client says before anything is recorded.</summary>
public enum CrossAccountFilingRefusal
{
    /// <summary>The source's server was last observed without <c>UIDPLUS</c>, so it cannot remove exactly the one message.</summary>
    SourceWithoutUidPlus = 0,

    /// <summary>The destination's server was last observed without <c>UIDPLUS</c>, so it cannot say which message it took.</summary>
    DestinationWithoutUidPlus = 1,

    /// <summary>One of the two accounts is being restored.</summary>
    AccountRestoring = 2,

    /// <summary>A replica still runs a build that does not know the overlay, so neither half can be committed locally first.</summary>
    UpgradeInProgress = 3,

    /// <summary>The person may read one of the two accounts but not move its mail.</summary>
    MoveNotPermitted = 4,

    /// <summary>The message is a sent copy or a draft, whose join to its outgoing record holds only on its own account.</summary>
    FiledFromOutgoingRecord = 5,

    /// <summary>The destination folder plays the drafts or the sent role, or lies beneath one that does.</summary>
    DestinationKeepsOutgoingMail = 6,
}
