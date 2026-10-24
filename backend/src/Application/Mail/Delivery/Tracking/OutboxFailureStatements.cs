// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Localization;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>Names the sentence an outbox entry states the failure it stopped on in.</summary>
/// <remarks>
/// A code is what a send records and what an operator looks up, and a person reading their outbox needs the sentence
/// instead. The failures a delivery attempt records each have one of their own; anything else — a failure an attempt
/// reached through a code shared with other paths — is stated as the send not having gone, which is true of all of them.
/// </remarks>
internal static class OutboxFailureStatements
{
    /// <summary>Names the sentence one recorded failure is stated in.</summary>
    /// <param name="failure">The failure the send stopped on.</param>
    /// <returns>The sentence's key.</returns>
    internal static ApplicationText TextOf(MailFathomErrorCode failure) => failure switch
    {
        _ when failure == MailFathomErrorCode.OutgoingEmailRefused => ApplicationText.OutboxFailureRefused,
        _ when failure == MailFathomErrorCode.OutgoingEmailAttemptsExhausted => ApplicationText.OutboxFailureAttemptsExhausted,
        _ when failure == MailFathomErrorCode.OutgoingEmailOutcomeUnknown => ApplicationText.OutboxFailureOutcomeUnknown,
        _ when failure == MailFathomErrorCode.OutgoingEmailDueTimeMissed => ApplicationText.OutboxFailureDueTimeMissed,
        _ when failure == MailFathomErrorCode.OutgoingEmailSenderUnconfigured => ApplicationText.OutboxFailureSenderUnconfigured,
        _ when failure == MailFathomErrorCode.OutgoingEmailBoundExceeded => ApplicationText.OutboxFailureBoundExceeded,
        _ when failure == MailFathomErrorCode.MailDeliveryUnavailable => ApplicationText.OutboxFailureServerUnreachable,
        _ => ApplicationText.OutboxFailureOther,
    };
}
