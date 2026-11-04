// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Scheduling;

namespace MailFathom.Application.Mail.Delivery.Submission;

/// <summary>Decides whether a caller's send may be held until the time it names, which both submission use cases ask alike.</summary>
/// <remarks>
/// A time that has gone and a time further ahead than the deployment holds a message are each the caller's own input,
/// so both are refused before anything is read or composed. A time in the past must not reach the outbox at all: a
/// record written for a moment already gone is one the next delivery pass takes immediately, which is the opposite of
/// what somebody naming a time asked for.
/// </remarks>
internal static class AuthoredSendHold
{
    /// <summary>Requires that a send may be held until the time it names.</summary>
    /// <param name="dueAt">The time the send is to leave at.</param>
    /// <param name="now">The moment the send is being asked for.</param>
    /// <param name="maximum">The longest a send may be held.</param>
    /// <exception cref="MailSubmissionRefusedException">Thrown when the time has passed or lies further ahead than <paramref name="maximum" />.</exception>
    internal static void Require(ZonedInstant dueAt, DateTimeOffset now, TimeSpan maximum)
    {
        if (dueAt.Instant <= now)
        {
            throw MailSubmissionRefusedException.DueTimeAlreadyPassed();
        }

        if (dueAt.Instant - now > maximum)
        {
            throw MailSubmissionRefusedException.DueTimeBeyondHold(maximum);
        }
    }
}
