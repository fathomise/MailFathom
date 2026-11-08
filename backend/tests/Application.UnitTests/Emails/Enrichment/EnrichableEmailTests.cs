// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Provenance;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Enrichment;

public sealed class EnrichableEmailTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 11, 6, 17, 40, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReceivedAt = new(2026, 11, 9, 8, 5, 0, TimeSpan.Zero);

    private static readonly EmailProvenance Received = StubEmailProvenanceReader.KnownCorrespondent;
    private static readonly EmailProvenance WrittenByMailbox =
        StubEmailProvenanceReader.KnownCorrespondent with { IsWrittenByMailbox = true };

    /// <summary>Mail somebody else wrote is dated by the server's clock, and by its own only where no hop dated it.</summary>
    [Fact]
    public void DatedAtOf_MailSomebodyElseWrote_PrefersTheArrivalOverTheSentInstant()
    {
        // Act
        var dated = EnrichableEmail.DatedAtOf(ReceivedAt, SentAt, Received);
        var undatedByAnyHop = EnrichableEmail.DatedAtOf(receivedAt: null, SentAt, Received);

        // Assert
        Assert.Equal(ReceivedAt, dated);
        Assert.Equal(SentAt, undatedByAnyHop);
    }

    /// <summary>
    /// Mail the mailbox wrote is dated by when it was sent whether or not the provider stamped an arrival on the copy,
    /// so a copy written offline on Friday and submitted on Monday still reads <em>before Monday</em> from Friday.
    /// </summary>
    [Fact]
    public void DatedAtOf_MailTheMailboxWrote_PrefersTheSentInstantOverTheArrival()
    {
        // Act
        var stampedByTheProvider = EnrichableEmail.DatedAtOf(ReceivedAt, SentAt, WrittenByMailbox);
        var filedByTheClient = EnrichableEmail.DatedAtOf(receivedAt: null, SentAt, WrittenByMailbox);
        var withoutADateHeader = EnrichableEmail.DatedAtOf(ReceivedAt, sentAt: null, WrittenByMailbox);

        // Assert
        Assert.Equal(SentAt, stampedByTheProvider);
        Assert.Equal(SentAt, filedByTheClient);
        Assert.Equal(ReceivedAt, withoutADateHeader);
    }

    /// <summary>A message carrying neither instant stays undated rather than being given one.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DatedAtOf_NeitherInstant_IsUndated(bool isWrittenByMailbox)
    {
        // Arrange
        var provenance = Received with { IsWrittenByMailbox = isWrittenByMailbox };

        // Act
        var dated = EnrichableEmail.DatedAtOf(receivedAt: null, sentAt: null, provenance);

        // Assert
        Assert.Null(dated);
    }
}
