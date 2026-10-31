// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access;

/// <summary>What naming a user's manager, or naming nobody, left the deployment holding.</summary>
public enum ManagerAssignmentResult
{
    /// <summary>The user's row names the manager, or nobody, once the write has run.</summary>
    Assigned = 0,

    /// <summary>The deployment holds no user under that identifier.</summary>
    UnknownUser = 1,

    /// <summary>The user was named as their own manager.</summary>
    OwnManager = 2,

    /// <summary>The user belongs to no organization, so nobody can be their manager.</summary>
    UserInNoOrganization = 3,

    /// <summary>The deployment holds nobody under the manager's identifier.</summary>
    UnknownManager = 4,

    /// <summary>The manager belongs to another organization than the user, or to none.</summary>
    ManagerOutsideOrganization = 5,

    /// <summary>The manager is a technical account rather than a person.</summary>
    ManagerNotAPerson = 6,

    /// <summary>The manager already reports to the user, directly or through others, so the line would close on itself.</summary>
    ReportingCycle = 7,
}
