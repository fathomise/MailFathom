// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What erasing a user did, and whether this process was serving them.</summary>
/// <remarks>
/// <para>
/// The second says whether removing the user also changed the running process. A served user is withheld from the
/// served-user cache before the erasure commits, so callers and synchronization stop reaching them without a restart.
/// </para>
/// <para>
/// It is named for the roster rather than for the erasure alone because the store answers only whether a record was
/// there to remove, and this is what the roster administration answered — including whether that record was served.
/// </para>
/// </remarks>
/// <param name="UserErased">Whether a user record was there to remove, so a repeat is reported as the no-op it is.</param>
/// <param name="WasServed">Whether the user that was erased was served, which is whether their record composed.</param>
internal readonly record struct UserRosterErasureOutcome(bool UserErased, bool WasServed);
