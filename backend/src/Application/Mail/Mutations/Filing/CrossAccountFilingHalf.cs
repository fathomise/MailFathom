// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Mutations.Filing;

/// <summary>How one account takes its half of a message filed into another account, as its custody phase decides.</summary>
public enum CrossAccountFilingHalf
{
    /// <summary>The half is a local commit and nothing converges for it, which is a held account's.</summary>
    CommittedLocally = 0,

    /// <summary>The half commits locally and a record carries it to the account's server, which is a mirrored account's.</summary>
    Carried = 1,

    /// <summary>Refused: the server was last observed without <c>UIDPLUS</c>, so it can neither name the appended copy nor remove exactly the one message.</summary>
    RefusedWithoutUidPlus = 2,

    /// <summary>Refused: the account is being restored, and its local state is still what its source is being filled from.</summary>
    RefusedWhileRestoring = 3,

    /// <summary>Refused: a replica still runs a build that does not know the overlay, so nothing commits locally first until the upgrade ends.</summary>
    RefusedDuringUpgrade = 4,
}
