// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Infrastructure.Mail.Mime;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

public sealed class RawHeaderFieldsTests
{
    /// <summary>Only the named field changes: every other header and the body keep their bytes, folding included.</summary>
    [Fact]
    public void Replace_AFieldTheMessageCarries_RewritesItAndLeavesEveryOtherByte()
    {
        // Arrange
        var rawMime = Encoding.ASCII.GetBytes(
            "From: anna@example.test\r\nDate: Fri, 6 Nov 2026 16:00:00 +0000\r\nSubject: a long\r\n subject\r\n\r\nDate: in the body\r\n");

        // Act
        var replaced = RawHeaderFields.Replace(rawMime, "Date", "Mon, 9 Nov 2026 09:00:00 +0000");

        // Assert
        Assert.Equal(
            "From: anna@example.test\r\nDate: Mon, 9 Nov 2026 09:00:00 +0000\r\nSubject: a long\r\n subject\r\n\r\nDate: in the body\r\n",
            Encoding.ASCII.GetString(replaced));
    }

    /// <summary>A folded field is replaced whole, its name is matched without regard to case, and a second instance is dropped.</summary>
    [Fact]
    public void Replace_AFoldedFieldWrittenTwice_LeavesOneNewField()
    {
        // Arrange
        var rawMime = Encoding.ASCII.GetBytes(
            "DATE: Fri, 6 Nov 2026\r\n 16:00:00 +0000\r\nDate-Received: kept\r\nSubject: s\r\ndate : Sat, 7 Nov 2026 10:00:00 +0000\r\n\r\nBody.");

        // Act
        var replaced = RawHeaderFields.Replace(rawMime, "Date", "Mon, 9 Nov 2026 09:00:00 +0000");

        // Assert
        Assert.Equal(
            "Date: Mon, 9 Nov 2026 09:00:00 +0000\r\nDate-Received: kept\r\nSubject: s\r\n\r\nBody.",
            Encoding.ASCII.GetString(replaced));
    }

    /// <summary>A message carrying no such field gains one at the top of its header block, in the line ending it uses.</summary>
    [Fact]
    public void Replace_AFieldTheMessageLacks_OpensTheHeaderBlockWithIt()
    {
        // Arrange
        var rawMime = Encoding.ASCII.GetBytes("Subject: s\n\nBody.");

        // Act
        var replaced = RawHeaderFields.Replace(rawMime, "Date", "Mon, 9 Nov 2026 09:00:00 +0000");

        // Assert
        Assert.Equal("Date: Mon, 9 Nov 2026 09:00:00 +0000\nSubject: s\n\nBody.", Encoding.ASCII.GetString(replaced));
    }

    /// <summary>Removing a field takes every instance and its folded lines out, and nothing else.</summary>
    [Fact]
    public void Replace_NoValue_RemovesEveryInstance()
    {
        // Arrange
        var rawMime = Encoding.ASCII.GetBytes(
            "MailFathom-Organization-Signature: v=1;\r\n b=AAAA\r\nFrom: anna@example.test\r\n\r\nBody.");

        // Act
        var replaced = RawHeaderFields.Replace(rawMime, "MailFathom-Organization-Signature", value: null);

        // Assert
        Assert.Equal("From: anna@example.test\r\n\r\nBody.", Encoding.ASCII.GetString(replaced));
    }
}
