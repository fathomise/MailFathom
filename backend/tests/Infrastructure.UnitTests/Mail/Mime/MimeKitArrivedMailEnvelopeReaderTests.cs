// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.Mime;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers what an arrived message's header block tells an automatic reply: who to answer, and whether to answer at all.</summary>
public sealed class MimeKitArrivedMailEnvelopeReaderTests
{
    private readonly MimeKitArrivedMailEnvelopeReader reader = new();

    /// <summary>An answer goes to <c>Reply-To</c> where the message names one, and carries the conversation it continues.</summary>
    [Fact]
    public void Read_AMessageNamingAReplyTo_AnswersThatAddressInItsConversation()
    {
        // Act
        var envelope = this.reader.Read(Mime(
            "From: Anna <anna@example.test>",
            "Reply-To: desk@example.test",
            "Subject: Budget",
            "Message-ID: <budget-2@example.test>",
            "References: <budget-1@example.test>"));

        // Assert
        Assert.NotNull(envelope);
        Assert.Equal("desk@example.test", envelope.ReplyTarget?.Address);
        Assert.Equal("Budget", envelope.Subject);
        Assert.Equal(EmailAutomation.None, envelope.Automation);
        Assert.False(envelope.HasNullReturnPath);
        Assert.Equal("budget-2@example.test", envelope.ThreadReferences.MessageId);
    }

    /// <summary>Without <c>Reply-To</c>, the answer goes to the first <c>From</c> mailbox.</summary>
    [Fact]
    public void Read_AMessageWithoutAReplyTo_AnswersTheAuthor()
    {
        // Act
        var envelope = this.reader.Read(Mime("From: Anna <anna@example.test>", "Subject: Hello"));

        // Assert
        Assert.Equal("anna@example.test", envelope?.ReplyTarget?.Address);
    }

    /// <summary>What a program, a list, or a bounce stamps on a message is read, which is what keeps it unanswered.</summary>
    [Theory]
    [InlineData("Auto-Submitted: auto-replied", EmailAutomation.AutomaticallySubmitted, false)]
    [InlineData("Auto-Submitted: no", EmailAutomation.None, false)]
    [InlineData("List-Id: <staff.example.test>", EmailAutomation.MailingList, false)]
    [InlineData("Precedence: bulk", EmailAutomation.BulkPrecedence, false)]
    [InlineData("Return-Path: <>", EmailAutomation.None, true)]
    public void Read_WhatAMessageClaimsAboutItself_IsReadFromItsHeaders(
        string header,
        EmailAutomation automation,
        bool hasNullReturnPath)
    {
        // Act
        var envelope = this.reader.Read(Mime(header, "From: anna@example.test"));

        // Assert
        Assert.Equal(automation, envelope?.Automation);
        Assert.Equal(hasNullReturnPath, envelope?.HasNullReturnPath);
    }

    private static ReadOnlyMemory<byte> Mime(params string[] headers) =>
        Encoding.ASCII.GetBytes(string.Join("\r\n", headers) + "\r\n\r\nI will be there.\r\n");
}
