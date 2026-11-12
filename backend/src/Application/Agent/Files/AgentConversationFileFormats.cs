// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Extraction.Images;

namespace MailFathom.Application.Agent.Files;

/// <summary>Decides what a file handed to a conversation is from its octets, never from the name or the type it arrived under.</summary>
/// <remarks>
/// <para>
/// A name and a declared type are whatever the sending machine wrote, so a document is recognized by its signature and,
/// for the formats that are a zip archive, by the parts the archive itself declares; a picture by the header
/// <see cref="ImageAttachmentHeader" /> reads for the image describer; and text by being text — valid UTF-8 with no NUL,
/// or opened by a UTF-16 byte-order mark. Markdown and CSV are text and are read as text, which is exactly how the
/// attachment text reader reads them.
/// </para>
/// <para>
/// Only what the reader reads, or what a chat request may carry, is recognized. A legacy Office file, an encrypted
/// package, an archive of anything else, and every other picture format answer <see langword="null" />, which the intake
/// refuses as a kind it does not take.
/// </para>
/// </remarks>
public static class AgentConversationFileFormats
{
    /// <summary>The most octets of a package's declared part types read to recognize it.</summary>
    private const int MaximumContentTypesOctets = 64 * 1024;

    /// <summary>The most octets of an OpenDocument package's declared type read to recognize it.</summary>
    private const int MaximumOpenDocumentTypeOctets = 128;

    private static readonly (string MainPartType, AttachmentDocumentFormat Format)[] OpenXmlMainParts =
    [
        ("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", AttachmentDocumentFormat.WordOpenXml),
        ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", AttachmentDocumentFormat.SpreadsheetOpenXml),
        ("application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml", AttachmentDocumentFormat.PresentationOpenXml),
    ];

    private static readonly Dictionary<string, AttachmentDocumentFormat> OpenDocumentTypes = new(StringComparer.Ordinal)
    {
        ["application/vnd.oasis.opendocument.text"] = AttachmentDocumentFormat.OpenDocumentText,
        ["application/vnd.oasis.opendocument.spreadsheet"] = AttachmentDocumentFormat.OpenDocumentSpreadsheet,
        ["application/vnd.oasis.opendocument.presentation"] = AttachmentDocumentFormat.OpenDocumentPresentation,
    };

    private static ReadOnlySpan<byte> PdfSignature => "%PDF-"u8;

    private static ReadOnlySpan<byte> ZipSignature => [0x50, 0x4B, 0x03, 0x04];

    private static ReadOnlySpan<byte> Utf16LittleEndianMark => [0xFF, 0xFE];

    private static ReadOnlySpan<byte> Utf16BigEndianMark => [0xFE, 0xFF];

    /// <summary>Reads what a file is from its octets.</summary>
    /// <param name="content">The whole file.</param>
    /// <returns>What it is, or <see langword="null" /> where it is nothing a conversation takes.</returns>
    public static RecognizedConversationFile? Recognize(ReadOnlyMemory<byte> content)
    {
        var octets = content.Span;

        if (octets.IsEmpty)
        {
            return null;
        }

        if (ImageAttachmentHeader.TryRead(octets, out var picture, out _))
        {
            return new RecognizedConversationFile(AgentConversationFileKind.Picture, picture.MediaType, DocumentFormat: null);
        }

        if (octets.StartsWith(PdfSignature))
        {
            return Document(AttachmentDocumentFormat.Pdf);
        }

        if (octets.StartsWith(ZipSignature))
        {
            return PackageFormatOf(content) is { } packaged ? Document(packaged) : null;
        }

        return IsText(octets) ? Document(AttachmentDocumentFormat.PlainText) : null;
    }

    /// <summary>Names the media type a recognized document format is read under.</summary>
    /// <param name="format">The format.</param>
    /// <returns>Its media type.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a format this recognition never answers.</exception>
    public static string MediaTypeOf(AttachmentDocumentFormat format) => format switch
    {
        AttachmentDocumentFormat.Pdf => "application/pdf",
        AttachmentDocumentFormat.WordOpenXml => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        AttachmentDocumentFormat.SpreadsheetOpenXml => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        AttachmentDocumentFormat.PresentationOpenXml => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        AttachmentDocumentFormat.OpenDocumentText => "application/vnd.oasis.opendocument.text",
        AttachmentDocumentFormat.OpenDocumentSpreadsheet => "application/vnd.oasis.opendocument.spreadsheet",
        AttachmentDocumentFormat.OpenDocumentPresentation => "application/vnd.oasis.opendocument.presentation",
        AttachmentDocumentFormat.PlainText => "text/plain",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not one a conversation's file is recognized as."),
    };

    private static RecognizedConversationFile Document(AttachmentDocumentFormat format) =>
        new(AgentConversationFileKind.Document, MediaTypeOf(format), format);

    /// <summary>Reads which document a zip archive is from the part types it declares, and nothing from its file names alone.</summary>
    /// <remarks>
    /// Only the archive's directory and one small declared part are read. A malformed archive is no package at all, which
    /// the intake refuses; what the reader would make of a well-formed one is left to the reader, under its own bounds.
    /// </remarks>
    private static AttachmentDocumentFormat? PackageFormatOf(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var stream = StreamOver(content);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            if (archive.GetEntry("[Content_Types].xml") is { } contentTypes)
            {
                var declared = ReadLeading(contentTypes, MaximumContentTypesOctets);

                return OpenXmlMainParts
                    .Where(part => declared.Contains(part.MainPartType, StringComparison.OrdinalIgnoreCase))
                    .Select(static part => (AttachmentDocumentFormat?)part.Format)
                    .FirstOrDefault();
            }

            return archive.GetEntry("mimetype") is { } mimetype
                && OpenDocumentTypes.TryGetValue(ReadLeading(mimetype, MaximumOpenDocumentTypeOctets).Trim(), out var format)
                    ? format
                    : null;
        }
        catch (Exception unreadable) when (unreadable is InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ReadLeading(ZipArchiveEntry entry, int maximumOctets)
    {
        using var opened = entry.Open();
        var buffer = new byte[maximumOctets];
        var read = opened.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static MemoryStream StreamOver(ReadOnlyMemory<byte> content) =>
        MemoryMarshal.TryGetArray(content, out var segment) && segment.Array is { } array
            ? new MemoryStream(array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(content.ToArray(), writable: false);

    private static bool IsText(ReadOnlySpan<byte> octets) =>
        octets.StartsWith(Utf16LittleEndianMark)
        || octets.StartsWith(Utf16BigEndianMark)
        || (!octets.Contains((byte)0) && Utf8.IsValid(octets));
}
