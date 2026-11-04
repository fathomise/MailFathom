// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery.Governance;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>What this deployment has decided about a caller asking it to send something.</summary>
/// <param name="UnvouchedRecipients">What to do about a recipient the caller named that nothing this deployment holds vouches for.</param>
/// <remarks>
/// A record rather than each value passed on its own, because what a caller may be talked into is a group of decisions
/// an operator makes together: the confirmation
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0013-what-a-caller-must-do-before-mail-leaves.md">ADR 0013</see>
/// leaves to its own contract belongs beside these rather than in a second registration nothing relates to it.
/// </remarks>
public sealed record AuthoredSendSettings(UnvouchedRecipientPosture UnvouchedRecipients)
{
    /// <summary>The span a caller's send may be held for where the operator stated none.</summary>
    /// <remarks>
    /// A quarter of a year, which holds every send somebody plans around a week, a month, or a season, and refuses the
    /// due time a year ahead that is a mistyped year far more often than it is a plan.
    /// </remarks>
    public static readonly TimeSpan DefaultMaxSendHold = TimeSpan.FromDays(90);

    /// <summary>Gets the settings of a deployment that decided nothing, which admit every recipient and hold a send for the default span.</summary>
    public static AuthoredSendSettings Permissive { get; } = new(UnvouchedRecipientPosture.Admit);

    /// <summary>Gets the longest a caller's send may be held before it leaves, measured from the moment it is asked for.</summary>
    /// <remarks>
    /// A held send keeps its composed message in the content store until it leaves, so the bound is a storage
    /// limitation as much as a guard against a mistyped date. It bounds what a caller asks for, and nothing a person
    /// schedules from the client's own composer.
    /// </remarks>
    public TimeSpan MaxSendHold { get; init; } = DefaultMaxSendHold;
}
