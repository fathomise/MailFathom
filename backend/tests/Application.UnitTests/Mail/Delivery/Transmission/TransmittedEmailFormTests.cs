// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Transmission;

public sealed class TransmittedEmailFormTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Recorded = new(2026, 11, 6, 16, 0, 0, TimeSpan.Zero);

    private static readonly byte[] StoredMime = Encoding.ASCII.GetBytes("Subject: s\r\n\r\nbody");

    /// <summary>A <c>Date</c> header carries whole seconds, so the instant an attempt dates its message with drops the rest, in UTC.</summary>
    [Fact]
    public void TransmissionInstantAt_AnInstantWithAFractionAndAnOffset_ReadsTheWholeSecondInUtc()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 11, 9, 10, 0, 5, TimeSpan.FromHours(1)).AddTicks(7_654_321);

        // Act
        var instant = TransmittedEmailForm.TransmissionInstantAt(now);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 9, 0, 5, TimeSpan.Zero), instant);
        Assert.Equal(TimeSpan.Zero, instant.Offset);
    }

    /// <summary>A sent copy is dated with the instant the record kept for its latest transmission.</summary>
    [Fact]
    public async Task ComposeSentCopyAsync_ASendWhoseTransmissionWasRecorded_DatesTheCopyWithIt()
    {
        // Arrange
        var transmittedAt = Recorded.AddDays(3);

        // Act
        var copy = await TransmittedEmailForms.Dating().ComposeSentCopyAsync(
            Send(transmittedAt),
            StoredMime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TransmittedEmailForms.DatedAt(StoredMime, transmittedAt), copy.ToArray());
    }

    /// <summary>A send an earlier build transmitted kept no instant, and its stored bytes are what it sent, so the copy is those bytes.</summary>
    [Fact]
    public async Task ComposeSentCopyAsync_ASendWithNoRecordedTransmission_FilesTheStoredMessage()
    {
        // Act
        var copy = await TransmittedEmailForms.Dating().ComposeSentCopyAsync(
            Send(transmittedAt: null),
            StoredMime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredMime, copy.ToArray());
    }

    private static OutgoingEmailRecord Send(DateTimeOffset? transmittedAt) => new()
    {
        Id = OutgoingEmailId.Create(Guid.NewGuid()),
        AccountId = Account,
        Requester = OutgoingEmailRequester.Command("send-1"),
        Principal = null,
        Recipients = [],
        Stage = OutgoingEmailStage.Sent,
        MimeByteLength = StoredMime.Length,
        AttemptCount = 1,
        RecordedAt = Recorded,
        StageChangedAt = Recorded,
        AvailableAt = Recorded,
        DueAt = null,
        LastFailure = null,
        LastReplyCode = null,
        Filings = [],
        LastFilingFailure = null,
        TransmittedAt = transmittedAt,
    };
}
