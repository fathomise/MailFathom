// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Localization;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Tracking;

/// <summary>Covers which sentence an outbox entry states each recorded failure in.</summary>
/// <remarks>
/// The sentences tell a person whether trying again could help, so two of them swapped would be an instruction reversed
/// rather than a wording slip — which is why every mapped code is held to its own sentence here.
/// </remarks>
public sealed class OutboxFailureStatementsTests
{
    public static TheoryData<MailFathomErrorCode, ApplicationText> FailuresADeliveryAttemptRecords => new()
    {
        { MailFathomErrorCode.OutgoingEmailRefused, ApplicationText.OutboxFailureRefused },
        { MailFathomErrorCode.OutgoingEmailAttemptsExhausted, ApplicationText.OutboxFailureAttemptsExhausted },
        { MailFathomErrorCode.OutgoingEmailOutcomeUnknown, ApplicationText.OutboxFailureOutcomeUnknown },
        { MailFathomErrorCode.OutgoingEmailDueTimeMissed, ApplicationText.OutboxFailureDueTimeMissed },
        { MailFathomErrorCode.OutgoingEmailSenderUnconfigured, ApplicationText.OutboxFailureSenderUnconfigured },
        { MailFathomErrorCode.OutgoingEmailBoundExceeded, ApplicationText.OutboxFailureBoundExceeded },
        { MailFathomErrorCode.MailDeliveryUnavailable, ApplicationText.OutboxFailureServerUnreachable },
    };

    [Theory]
    [MemberData(nameof(FailuresADeliveryAttemptRecords))]
    public void TextOf_AFailureADeliveryAttemptRecords_IsStatedInItsOwnSentence(
        MailFathomErrorCode failure,
        ApplicationText expected)
    {
        // Act
        var text = OutboxFailureStatements.TextOf(failure);

        // Assert
        Assert.Equal(expected, text);
    }

    /// <summary>A code other paths share is stated as the send not having gone, which is true whatever reached it.</summary>
    [Fact]
    public void TextOf_ACodeSharedWithOtherPaths_IsStatedAsTheSendNotHavingGone()
    {
        // Act
        var text = OutboxFailureStatements.TextOf(MailFathomErrorCode.MailTransportSecurityPolicyViolated);

        // Assert
        Assert.Equal(ApplicationText.OutboxFailureOther, text);
    }
}
