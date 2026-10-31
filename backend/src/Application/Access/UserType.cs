// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access;

/// <summary>Whether a user this deployment holds is a person or a technical account.</summary>
/// <remarks>
/// The organization's statement about the user rather than the user's own, so it is written by an administrator alone
/// and governed by no settings policy. What it decides is whether the user is somebody their colleagues correspond with:
/// a service account has no member contact in the organization's directory and is never suggested as a recipient.
/// </remarks>
public enum UserType
{
    /// <summary>A person, which is what every user is recorded as until an administrator says otherwise.</summary>
    User = 0,

    /// <summary>A technical account serving a mailbox or an integration rather than a person.</summary>
    ServiceAccount = 1,
}
