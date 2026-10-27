// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Infrastructure.Mail.Mime;
using MimeKit;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

public sealed class InternetMessageIdReaderTests
{
    /// <summary>The send's record and the arrival read one identifier the same way, without the brackets either wrote.</summary>
    [Fact]
    public async Task ReadAsync_RawMimeAndItsParsedHeaders_ReadTheSameIdentifier()
    {
        // Arrange
        var rawMime = Encoding.ASCII.GetBytes("Message-ID:  <signed-1@org.example.test> \r\nSubject: s\r\n\r\nBody.\r\n");
        using var source = new MemoryStream(rawMime);
        using var parsed = await MimeMessage.LoadAsync(source, TestContext.Current.CancellationToken);

        // Act
        var fromBytes = await InternetMessageIdReader.ReadAsync(rawMime, TestContext.Current.CancellationToken);
        var fromHeaders = InternetMessageIdReader.Read(parsed.Headers);

        // Assert
        Assert.Equal("signed-1@org.example.test", fromBytes);
        Assert.Equal(fromBytes, fromHeaders);
    }

    /// <summary>A message with no identifier, or an empty one, names none.</summary>
    [Theory]
    [InlineData("Subject: s\r\n\r\nBody.\r\n")]
    [InlineData("Message-ID: <>\r\n\r\nBody.\r\n")]
    public async Task ReadAsync_MessageCarryingNoUsableIdentifier_ReadsNone(string written)
    {
        // Act
        var identifier = await InternetMessageIdReader.ReadAsync(Encoding.ASCII.GetBytes(written), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(identifier);
    }

    /// <summary>An identifier past the stored bound is treated as none, which can only refuse a lookup and never grant one.</summary>
    [Fact]
    public async Task ReadAsync_IdentifierLongerThanTheBound_ReadsNone()
    {
        // Arrange
        var written = $"Message-ID: <{new string('a', InternetMessageIdReader.MaximumLength)}@example.test>\r\n\r\nBody.\r\n";

        // Act
        var identifier = await InternetMessageIdReader.ReadAsync(Encoding.ASCII.GetBytes(written), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(identifier);
    }
}
