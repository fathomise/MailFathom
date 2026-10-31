// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Presence;

namespace MailFathom.Application.Contacts.Members;

/// <summary>One colleague as a reader sees them: who they are, and whether they are around.</summary>
/// <param name="Contact">The member as the directory composes them from their user record.</param>
/// <param name="Presence">Their presence as every other member sees it, which never reads <see cref="PresenceState.Hidden" />.</param>
/// <param name="OutOfOfficeUntil">When the member's automatic reply stops answering, while it is switched on and in its period, and <see langword="null" /> otherwise — never what it answers with.</param>
public sealed record Colleague(MemberContact Contact, PresenceReading Presence, DateTimeOffset? OutOfOfficeUntil);
