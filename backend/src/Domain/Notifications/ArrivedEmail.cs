// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Notifications;

/// <summary>One message that arrived for a person, reduced to what a notification about it says.</summary>
/// <remarks>
/// The sender and the subject are the whole of what is read from the message, and both are read when the notification
/// is produced rather than when it is drawn, so the centre re-reads no mail. Nothing else a message carries — a body, an
/// attachment, a recipient — has a place here.
/// </remarks>
public sealed record ArrivedEmail
{
    /// <summary>Initializes an arrived email.</summary>
    /// <param name="email">The stored message that arrived.</param>
    /// <param name="sender">Who sent it, by the name the message gives them or, where it gives none, by their address; <see langword="null" /> where it was not read.</param>
    /// <param name="subject">Its subject line, or <see langword="null" /> where it carried none or was not read.</param>
    public ArrivedEmail(StoredEmailId email, string? sender, string? subject)
    {
        this.Email = email;
        this.Sender = string.IsNullOrWhiteSpace(sender) ? null : sender.Trim();
        this.Subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
    }

    /// <summary>Gets the stored message that arrived.</summary>
    public StoredEmailId Email { get; }

    /// <summary>Gets who sent it, and <see langword="null" /> where the message names nobody or was not read.</summary>
    /// <remarks>
    /// An arrival larger than <see cref="MailArrivalFold.MaximumSeparateArrivals" /> is merged whatever stands, so its
    /// messages are not read for a sender nobody will be shown.
    /// </remarks>
    public string? Sender { get; }

    /// <summary>Gets its subject line, and <see langword="null" /> where it carried none.</summary>
    public string? Subject { get; }
}
