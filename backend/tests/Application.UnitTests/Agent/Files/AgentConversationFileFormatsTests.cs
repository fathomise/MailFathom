// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.IO.Compression;
using System.Text;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Files;

/// <summary>Covers what a file is read as from its octets alone, whatever it was called.</summary>
public sealed class AgentConversationFileFormatsTests
{
    [Fact]
    public void Recognize_APicture_ReadsItAsThePictureItsHeaderProves()
    {
        // Act
        var recognized = AgentConversationFileFormats.Recognize(SyntheticImages.Png(16, 9));

        // Assert
        Assert.Equal(new RecognizedConversationFile(AgentConversationFileKind.Picture, "image/png", DocumentFormat: null), recognized);
    }

    [Theory]
    [InlineData("%PDF-1.7\n", AttachmentDocumentFormat.Pdf)]
    [InlineData("Plain notes, with a zł and an é.", AttachmentDocumentFormat.PlainText)]
    public void Recognize_ADocumentsLeadingOctets_ReadsItAsTheFormatTheyProve(string leading, AttachmentDocumentFormat expected)
    {
        // Act
        var recognized = AgentConversationFileFormats.Recognize(Encoding.UTF8.GetBytes(leading));

        // Assert
        Assert.Equal((AgentConversationFileKind.Document, expected), (recognized?.Kind, recognized?.DocumentFormat));
    }

    /// <summary>A package is read by the part types it declares, so an archive declaring none of a document's is no file a conversation takes.</summary>
    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", AttachmentDocumentFormat.WordOpenXml)]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", AttachmentDocumentFormat.SpreadsheetOpenXml)]
    [InlineData("application/xml", null)]
    public void Recognize_AZipPackage_ReadsItByTheMainPartItDeclares(string mainPartType, AttachmentDocumentFormat? expected)
    {
        // Arrange
        var package = Package("[Content_Types].xml", $"<Types><Override PartName=\"/main.xml\" ContentType=\"{mainPartType}\"/></Types>");

        // Act
        var recognized = AgentConversationFileFormats.Recognize(package);

        // Assert
        Assert.Equal(expected, recognized?.DocumentFormat);
    }

    [Fact]
    public void Recognize_AnOpenDocumentPackage_ReadsItByTheTypeItDeclares()
    {
        // Act
        var recognized = AgentConversationFileFormats.Recognize(Package("mimetype", "application/vnd.oasis.opendocument.text"));

        // Assert
        Assert.Equal(AttachmentDocumentFormat.OpenDocumentText, recognized?.DocumentFormat);
    }

    /// <summary>Octets that are neither a picture, a package, a PDF, nor text — a binary, or an archive cut short — are refused rather than guessed at.</summary>
    [Theory]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x00, 0x02 })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 })]
    [InlineData(new byte[] { 0xC3, 0x28 })]
    public void Recognize_OctetsNoConversationTakes_AnswersNothing(byte[] content)
    {
        // Act
        var recognized = AgentConversationFileFormats.Recognize(content);

        // Assert
        Assert.Null(recognized);
    }

    private static byte[] Package(string entryName, string entryText)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry(entryName).Open();
            entry.Write(Encoding.UTF8.GetBytes(entryText));
        }

        return buffer.ToArray();
    }
}
