// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Categories;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Exports;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Export;

/// <summary>What one archive holds, read back from the bytes rather than from the writer's own bookkeeping.</summary>
/// <remarks>
/// A person leaving the product opens this archive in whatever their operating system gives them, so every assertion
/// here reads it as an ordinary zip: the entries by name, and a message's content compared against the stored bytes it
/// was written from.
/// </remarks>
public sealed class MailboxExportArchiveWriterTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2023, 11, 14, 22, 13, 20, TimeSpan.Zero);

    private static readonly MailboxExportFolder Inbox = new("INBOX", ["INBOX"], IsInbox: true);

    [Fact]
    public async Task WriteMessageAsync_AStoredMessage_CarriesItsBytesThroughUnchanged()
    {
        // Arrange
        var stored = Encoding.ASCII.GetBytes("From: sender@example.test\r\nSubject: Stored\r\n\r\nBody.\r\n");
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(Inbox);
            await writer.WriteMessageAsync(
                directory,
                MessageIn(Inbox, stored.Length),
                stored,
                ordinal: 7,
                TestContext.Current.CancellationToken);

            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var entry = Assert.Single(
            archive.Entries,
            held => held.FullName.StartsWith("cur/", StringComparison.Ordinal) && !held.FullName.EndsWith('/'));

        Assert.Equal(stored, await ReadAsync(entry));
    }

    [Fact]
    public async Task WriteMessageAsync_TheInbox_WritesItAsTheRootMaildirWithItsThreeSubdirectories()
    {
        // Arrange
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            writer.OpenFolder(Inbox);
            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);

        Assert.Equal(
            ["cur/", "keywords.json", "new/", "tmp/"],
            archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WriteMessageAsync_ANestedFolder_WritesOneMaildirPlusPlusDirectoryAtTheArchiveRoot()
    {
        // Arrange
        var projects = new MailboxExportFolder("Projects/MailFathom", ["Projects", "MailFathom"], IsInbox: false);
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(projects);
            await writer.WriteMessageAsync(
                directory,
                MessageIn(projects, byteLength: 4),
                new byte[] { 1, 2, 3, 4 },
                ordinal: 0,
                TestContext.Current.CancellationToken);

            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);

        Assert.Equal(
            [".Projects.MailFathom/cur/", ".Projects.MailFathom/new/", ".Projects.MailFathom/tmp/"],
            archive.Entries
                .Select(entry => entry.FullName)
                .Where(name => name.EndsWith('/'))
                .Order(StringComparer.Ordinal));

        Assert.Single(
            archive.Entries,
            entry => entry.FullName.StartsWith(".Projects.MailFathom/cur/", StringComparison.Ordinal)
                && !entry.FullName.EndsWith('/'));
    }

    /// <summary>A folder name is untrusted text, and nothing an extractor reads as structure may survive into a path.</summary>
    [Theory]
    [InlineData("..", ".%2E%2E/cur/")]
    [InlineData("a/b", ".a%2Fb/cur/")]
    [InlineData("a\\b", ".a%5Cb/cur/")]
    [InlineData("Zamówienia", ".Zam%C3%B3wienia/cur/")]
    public async Task OpenFolder_AFolderNamedWithStructureOrNonAscii_EncodesEveryByteOutsideTheUnreservedSet(
        string folderName,
        string expectedDirectory)
    {
        // Arrange
        var folder = new MailboxExportFolder(folderName, [folderName], IsInbox: false);
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            writer.OpenFolder(folder);
            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);

        Assert.Contains(expectedDirectory, archive.Entries.Select(entry => entry.FullName), StringComparer.Ordinal);
    }

    [Fact]
    public async Task WriteMessageAsync_AMessageCarryingFlags_NamesTheFileWithItsArrivalLengthAndFlagLetters()
    {
        // Arrange
        var stored = new byte[4096];
        var destination = new MemoryStream();
        var message = MessageIn(Inbox, stored.Length) with
        {
            Flags = MaildirFlagSet.Seen | MaildirFlagSet.Draft | MaildirFlagSet.Answered,
        };

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(Inbox);
            await writer.WriteMessageAsync(
                directory,
                message,
                stored,
                ordinal: 7,
                TestContext.Current.CancellationToken);

            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);

        Assert.Contains(
            "cur/1700000000.7.mailfathom,S=4096:2,DRS",
            archive.Entries.Select(entry => entry.FullName),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task WriteKeywordsAsync_MessagesCarryingKeywords_NamesEachOneAgainstThePathItWasWrittenAt()
    {
        // Arrange
        var destination = new MemoryStream();
        var keyworded = MessageIn(Inbox, byteLength: 2) with { Keywords = ["Invoices", "$Important"] };
        string writtenPath;

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(Inbox);

            writtenPath = await writer.WriteMessageAsync(
                directory,
                keyworded,
                new byte[] { 1, 2 },
                ordinal: 0,
                TestContext.Current.CancellationToken);

            await writer.WriteMessageAsync(
                directory,
                MessageIn(Inbox, byteLength: 2),
                new byte[] { 3, 4 },
                ordinal: 1,
                TestContext.Current.CancellationToken);

            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var document = JsonDocument.Parse(await ReadAsync(EntryNamed(archive, "keywords.json")));
        var named = document.RootElement.GetProperty("messages");

        var only = Assert.Single(named.EnumerateArray());
        Assert.Equal(writtenPath, only.GetProperty("path").GetString());
        Assert.Equal(
            ["Invoices", "$Important"],
            only.GetProperty("keywords").EnumerateArray().Select(keyword => keyword.GetString()));
    }

    /// <summary>An empty document says the export found no keyword, which a missing one does not.</summary>
    [Fact]
    public async Task WriteKeywordsAsync_AMailboxCarryingNoKeyword_StillWritesTheDocument()
    {
        // Arrange
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            writer.OpenFolder(Inbox);
            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var document = JsonDocument.Parse(await ReadAsync(EntryNamed(archive, "keywords.json")));

        Assert.Empty(document.RootElement.GetProperty("messages").EnumerateArray());
    }

    /// <summary>What a delete left on the server is still something the deployment holds, so the archive says how much of it there is.</summary>
    [Fact]
    public async Task WriteForgottenOccurrencesAsync_OccurrencesAreRemembered_ReportsHowMany()
    {
        // Arrange
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            await writer.WriteForgottenOccurrencesAsync(3, TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var document = JsonDocument.Parse(await ReadAsync(EntryNamed(archive, "forgotten-occurrences.json")));

        Assert.Equal(3, document.RootElement.GetProperty("count").GetInt64());
    }

    /// <summary>Every category of a message is carried with what said so, named against the path the message was written at.</summary>
    [Fact]
    public async Task WriteCategoriesAsync_AMessageHoldingCategories_NamesThemAgainstItsPathWithTheirProvenance()
    {
        // Arrange
        var destination = new MemoryStream();
        var message = MessageIn(Inbox, 2) with
        {
            Categories = [Category(EmailCategoryKind.Default, "finance"), Category(EmailCategoryKind.Own, "Contracts")],
        };
        string writtenPath;

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(Inbox);
            writtenPath = await writer.WriteMessageAsync(directory, message, new byte[] { 1, 2 }, 0, TestContext.Current.CancellationToken);
            await writer.WriteCategoriesAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var document = JsonDocument.Parse(await ReadAsync(EntryNamed(archive, "categories/000000.json")));
        var only = Assert.Single(document.RootElement.GetProperty("messages").EnumerateArray());
        var categories = only.GetProperty("categories").EnumerateArray().ToArray();

        Assert.Equal(writtenPath, only.GetProperty("path").GetString());
        Assert.Equal(["finance", "Contracts"], categories.Select(category => category.GetProperty("category").GetString()));
        Assert.Equal("Default", categories[0].GetProperty("kind").GetString());
        Assert.Equal(0.9, categories[0].GetProperty("probability").GetDouble());
        Assert.Equal("Model", categories[0].GetProperty("source").GetString());
        Assert.Equal("provider/build", categories[0].GetProperty("origin").GetString());
        Assert.Equal("abc123", categories[0].GetProperty("questionRevision").GetString());
    }

    /// <summary>An empty document says the export found no category, which a missing one does not.</summary>
    [Fact]
    public async Task WriteCategoriesAsync_AMailboxCarryingNoCategory_StillWritesADocument()
    {
        // Arrange
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            writer.OpenFolder(Inbox);
            await writer.WriteCategoriesAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var document = JsonDocument.Parse(await ReadAsync(EntryNamed(archive, "categories/000000.json")));

        Assert.Empty(document.RootElement.GetProperty("messages").EnumerateArray());
    }

    /// <summary>On a categorizing account every message carries one, so what is held for them is let go a document at a time, and none is left empty behind a full one.</summary>
    [Fact]
    public async Task WriteCategoriesAsync_MoreMessagesThanOneDocumentNames_SplitsThemAcrossDocumentsLosingNone()
    {
        // Arrange
        var destination = new MemoryStream();
        var message = MessageIn(Inbox, 1) with { Categories = [Category(EmailCategoryKind.Default, "other")] };

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(Inbox);

            foreach (var ordinal in Enumerable.Range(0, 2000))
            {
                await writer.WriteMessageAsync(directory, message, new byte[] { 1 }, ordinal, TestContext.Current.CancellationToken);
            }

            await writer.WriteCategoriesAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var documents = archive.Entries
            .Where(entry => entry.FullName.StartsWith("categories/", StringComparison.Ordinal))
            .ToArray();
        var named = 0;

        foreach (var entry in documents)
        {
            named += JsonDocument.Parse(await ReadAsync(entry)).RootElement.GetProperty("messages").GetArrayLength();
        }

        Assert.Equal(2, documents.Length);
        Assert.Equal(2000, named);
    }

    /// <summary>
    /// An arrival time is a header the remote server composed, and a zip entry's timestamp holds 1980 to 2107 and
    /// nothing else. One message a server dated outside that would otherwise raise, and — being raised inside the job —
    /// would fail every attempt to export the account it is in, which for a drained mailbox is its only way out of the
    /// product. The file name still carries the instant the message states, so nothing about it is lost.
    /// <para>
    /// The third case is the one an instant comparison alone would miss: stated at a negative offset it names an
    /// instant inside the range whose own components fall before it, and a zip entry's timestamp is those components.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("1969-07-20T20:17:40Z", "1980-01-01T00:00:00Z")]
    [InlineData("2223-06-01T09:00:00Z", "2107-12-31T23:59:58Z")]
    [InlineData("1979-12-31T23:00:00-02:00", "1980-01-01T01:00:00Z")]
    public async Task WriteMessageAsync_AnArrivalTimeAZipEntryCannotHold_StampsTheEntryAtTheBoundAndNamesTheFileForTheInstant(
        string receivedAt,
        string expectedStamp)
    {
        // Arrange
        var arrival = DateTimeOffset.Parse(receivedAt, CultureInfo.InvariantCulture);
        var stored = Encoding.ASCII.GetBytes("From: sender@example.test\r\n\r\nBody.\r\n");
        var destination = new MemoryStream();

        // Act
        await using (var writer = new MailboxExportArchiveWriter(destination))
        {
            var directory = writer.OpenFolder(Inbox);
            await writer.WriteMessageAsync(
                directory,
                MessageIn(Inbox, stored.Length) with { ReceivedAt = arrival },
                stored,
                ordinal: 1,
                TestContext.Current.CancellationToken);

            await writer.WriteKeywordsAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        using var archive = OpenArchive(destination);
        var entry = Assert.Single(
            archive.Entries,
            candidate => candidate.FullName.StartsWith("cur/", StringComparison.Ordinal) && !candidate.FullName.EndsWith('/'));

        // Compared as a wall clock rather than as an instant: a zip entry's timestamp carries no zone, so the reader
        // hands it back under whichever offset the machine runs at.
        Assert.Equal(
            DateTimeOffset.Parse(expectedStamp, CultureInfo.InvariantCulture).UtcDateTime,
            entry.LastWriteTime.DateTime);
        Assert.Contains(
            arrival.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            entry.Name,
            StringComparison.Ordinal);
    }

    private static ExportableMessage MessageIn(MailboxExportFolder folder, long byteLength) => new(
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001")),
        folder,
        ReceivedAt,
        byteLength,
        MaildirFlagSet.None,
        [],
        []);

    private static ExportedEmailCategory Category(EmailCategoryKind kind, string category) =>
        new(kind, category, 0.9, EmailCategorySource.Model, "provider/build", "abc123");

    private static ZipArchive OpenArchive(MemoryStream destination) =>
        new(new MemoryStream(destination.ToArray(), writable: false), ZipArchiveMode.Read);

    private static ZipArchiveEntry EntryNamed(ZipArchive archive, string name) =>
        Assert.Single(archive.Entries, entry => string.Equals(entry.FullName, name, StringComparison.Ordinal));

    private static async Task<byte[]> ReadAsync(ZipArchiveEntry entry)
    {
        await using var content = entry.Open();
        using var buffer = new MemoryStream();

        await content.CopyToAsync(buffer, TestContext.Current.CancellationToken);

        return buffer.ToArray();
    }
}
