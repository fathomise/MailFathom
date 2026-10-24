// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Notifications;

/// <summary>A notification an arrival raises, with the messages a merged one names beside it.</summary>
/// <param name="Notification">The notification to keep.</param>
/// <param name="NamedEmails">The messages it names, which is empty for a notification about one message: that one names its message as its target.</param>
public sealed record RaisedMailArrival(Notification Notification, IReadOnlyList<StoredEmailId> NamedEmails);
