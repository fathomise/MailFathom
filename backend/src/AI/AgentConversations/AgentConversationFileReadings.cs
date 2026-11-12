// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.AI.AgentConversations;

/// <summary>Writes what the Agent is shown of the files a person handed to a conversation: the turn's list of them, a page of one, and what a search of them found.</summary>
/// <remarks>
/// Every name and every word here came from the person's own machine and is quoted, inside elements this service
/// writes, after the guard has passed over it. A file is identified by the id the deployment minted for it rather than
/// by its name, so a name can neither collide with another file's nor claim to be one.
/// </remarks>
internal static class AgentConversationFileReadings
{
    /// <summary>Names the element the turn lists a conversation's files in.</summary>
    internal const string ConversationFilesElementName = "conversation-files";

    /// <summary>Names the element one of those files is written as.</summary>
    internal const string ConversationFileElementName = "conversation-file";

    /// <summary>Names the element one page of a conversation's file is written as.</summary>
    internal const string FilePageElementName = "file-page";

    /// <summary>Names the element a conversation's file with no words to read is written as.</summary>
    internal const string FileUnreadElementName = "file-unread";

    /// <summary>Names the element what a search of the files found is written as.</summary>
    internal const string FileSearchElementName = "file-search";

    /// <summary>Names the element one passage a search found is written as.</summary>
    internal const string PassageElementName = "passage";

    /// <summary>Names the attribute carrying a file's id, which a reading tool takes.</summary>
    internal const string FileIdAttributeName = "file-id";

    /// <summary>Names the attribute saying what a file is.</summary>
    internal const string KindAttributeName = "kind";

    /// <summary>Names the attribute saying the question this turn answers carries the file.</summary>
    internal const string CarriedAttributeName = "carried";

    /// <summary>Names the attribute saying how a document's words reach the model.</summary>
    internal const string WordsAttributeName = "words";

    /// <summary>The value of <see cref="WordsAttributeName" /> for a document given to the turn whole.</summary>
    internal const string WholeWords = "whole";

    /// <summary>The value of <see cref="WordsAttributeName" /> for a document read a page at a time and searched.</summary>
    internal const string LongWords = "long";

    /// <summary>Names the attribute saying whether a carried picture was shown with this turn.</summary>
    internal const string ShownAttributeName = "shown";

    /// <summary>Names the attribute saying how a search reached a passage.</summary>
    internal const string FoundByAttributeName = "found-by";

    /// <summary>Names the attribute saying whether a search could look by meaning as well as by words.</summary>
    internal const string ByMeaningAttributeName = "by-meaning";

    /// <summary>Writes the files a conversation holds into a turn, each with how it reaches the model.</summary>
    /// <param name="document">The turn being written.</param>
    /// <param name="files">The files, every name and word already guarded.</param>
    internal static void WriteHeld(AgentTurnDocument document, IReadOnlyList<GuardedConversationFile> files)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count is 0)
        {
            return;
        }

        document.StartElement(ConversationFilesElementName);

        foreach (var file in files)
        {
            document.StartElement(ConversationFileElementName);
            WriteIdentity(document, file.File, file.Name);
            WriteTrue(document, CarriedAttributeName, file.Carried);

            if (file.File.Kind is AgentConversationFileKind.Picture)
            {
                if (file.Carried)
                {
                    document.WriteAttribute(ShownAttributeName, file.Shown ? "true" : "false");
                }
            }
            else if (file.File.Reading is { WasRead: true } reading)
            {
                document.WriteAttribute(WordsAttributeName, reading.IsLong ? LongWords : WholeWords);
                WritePlaces(document, reading.PageCount, reading.PageKind);
                document.WriteAttribute(AgentConversationReadings.CharactersAttributeName, reading.TextLength.ToString(CultureInfo.InvariantCulture));
                WriteParts(document, file.WholeText, file.WholeTextCut);
            }
            else
            {
                document.WriteAttribute(AgentConversationReadings.UnreadAttributeName, UnreadReason(file.File.Reading));
            }

            document.EndElement();
        }

        document.EndElement();
    }

    /// <summary>Writes one page of a conversation's document, each part of it under the page, slide, or sheet it came from.</summary>
    /// <param name="file">The document.</param>
    /// <param name="name">Its guarded name, or <see langword="null" /> where it has none.</param>
    /// <param name="bounds">Where the page lies in the document's words.</param>
    /// <param name="parts">The page cut at the places it crosses, every part already guarded.</param>
    /// <returns>The document.</returns>
    internal static string FilePage(
        AgentConversationFile file,
        string? name,
        PageBounds bounds,
        IReadOnlyList<GuardedAttachmentPart> parts)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(parts);

        using var document = new AgentTurnDocument();

        document.StartElement(FilePageElementName);
        WriteIdentity(document, file, name);

        if (file.Reading is { } reading)
        {
            WritePlaces(document, reading.PageCount, reading.PageKind);
        }

        document.WriteAttribute(AgentConversationReadings.CharactersAttributeName, bounds.Characters.ToString(CultureInfo.InvariantCulture));
        document.WriteAttribute(AgentConversationReadings.PageStartAttributeName, bounds.Start.ToString(CultureInfo.InvariantCulture));
        document.WriteAttribute(AgentConversationReadings.PageEndAttributeName, bounds.End.ToString(CultureInfo.InvariantCulture));

        if (bounds.End < bounds.Characters)
        {
            document.WriteAttribute(AgentConversationReadings.NextAttributeName, bounds.End.ToString(CultureInfo.InvariantCulture));
        }

        WriteTrue(document, AgentConversationReadings.CeilingReachedAttributeName, bounds.CeilingReached);
        WriteParts(document, parts, alreadyCut: false);
        document.EndElement();

        return document.Complete();
    }

    /// <summary>Writes that a conversation's file has no words to read, and why.</summary>
    /// <param name="file">The file.</param>
    /// <param name="name">Its guarded name, or <see langword="null" /> where it has none.</param>
    /// <returns>The document.</returns>
    /// <remarks>A picture is named as one here, because its words are nothing anybody read: it was shown with the question that carried it.</remarks>
    internal static string FileUnread(AgentConversationFile file, string? name)
    {
        ArgumentNullException.ThrowIfNull(file);

        using var document = new AgentTurnDocument();

        document.StartElement(FileUnreadElementName);
        WriteIdentity(document, file, name);
        document.WriteAttribute(
            AgentConversationReadings.UnreadAttributeName,
            file.Kind is AgentConversationFileKind.Picture ? "picture" : UnreadReason(file.Reading));
        document.EndElement();

        return document.Complete();
    }

    /// <summary>Writes what a search of a conversation's files found, best first.</summary>
    /// <param name="byMeaning">Whether the search could look by meaning as well as by words.</param>
    /// <param name="passages">The passages, every name and word already guarded.</param>
    /// <returns>The document.</returns>
    internal static string FileSearch(bool byMeaning, IReadOnlyList<GuardedFilePassage> passages)
    {
        ArgumentNullException.ThrowIfNull(passages);

        using var document = new AgentTurnDocument();

        document.StartElement(FileSearchElementName);
        document.WriteAttribute(ByMeaningAttributeName, byMeaning ? "true" : "false");

        foreach (var passage in passages)
        {
            document.StartElement(PassageElementName);
            document.WriteAttribute(FileIdAttributeName, passage.File.ToString());
            WriteOptional(document, AgentConversationReadings.FileAttributeName, passage.Name);
            WriteSegment(document, passage.Segment);
            document.WriteAttribute(AgentConversationReadings.PageStartAttributeName, passage.Start.ToString(CultureInfo.InvariantCulture));
            document.WriteAttribute(AgentConversationReadings.PageEndAttributeName, passage.End.ToString(CultureInfo.InvariantCulture));
            document.WriteAttribute(FoundByAttributeName, passage.FoundBy is AgentConversationFileFoundBy.Meaning ? "meaning" : "words");
            document.WriteQuotedContent(passage.Text, passage.Cut);
            document.EndElement();
        }

        document.EndElement();

        return document.Complete();
    }

    private static void WriteIdentity(AgentTurnDocument document, AgentConversationFile file, string? name)
    {
        document.WriteAttribute(FileIdAttributeName, file.Id.ToString());
        WriteOptional(document, AgentConversationReadings.FileAttributeName, name);
        document.WriteAttribute(KindAttributeName, file.Kind is AgentConversationFileKind.Picture ? "picture" : "document");
        document.WriteAttribute(AgentConversationReadings.MediaTypeAttributeName, file.MediaType);
        document.WriteAttribute(AgentConversationReadings.SizeAttributeName, file.ByteLength.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteParts(AgentTurnDocument document, IReadOnlyList<GuardedAttachmentPart>? parts, bool alreadyCut)
    {
        if (parts is null)
        {
            return;
        }

        foreach (var (part, index) in parts.Select(static (part, index) => (part, index)))
        {
            document.StartElement(AgentConversationReadings.PartElementName);
            WriteSegment(document, part.Segment);
            document.WriteQuotedContent(part.Text, alreadyCut && index == parts.Count - 1);
            document.EndElement();
        }
    }

    private static void WriteSegment(AgentTurnDocument document, AttachmentTextSegment? segment)
    {
        if (segment is { } place)
        {
            document.WriteAttribute(AgentConversationReadings.SegmentAttributeName, place.Kind.ToString());
            document.WriteAttribute(AgentConversationReadings.SegmentNumberAttributeName, place.Number.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void WritePlaces(AgentTurnDocument document, int pageCount, AttachmentTextSegmentKind? pageKind)
    {
        if (pageCount > 0)
        {
            document.WriteAttribute(AgentConversationReadings.PagesAttributeName, pageCount.ToString(CultureInfo.InvariantCulture));
        }

        WriteOptional(document, AgentConversationReadings.PageKindAttributeName, pageKind?.ToString());
    }

    private static void WriteOptional(AgentTurnDocument document, string attributeName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            document.WriteAttribute(attributeName, value);
        }
    }

    private static void WriteTrue(AgentTurnDocument document, string attributeName, bool holds)
    {
        if (holds)
        {
            document.WriteAttribute(attributeName, "true");
        }
    }

    /// <summary>Names why a document has no words: what the reader said of it, or that it holds none.</summary>
    private static string UnreadReason(AgentConversationFileReading? reading) => reading?.Outcome switch
    {
        null => AgentConversationReadings.NeverReadReason,
        AttachmentTextExtractionOutcome.Extracted => "no-words",
        { } outcome => outcome.ToString(),
    };
}

/// <summary>One file of a conversation as a turn names it, its name and any words given whole already guarded.</summary>
/// <param name="File">The file.</param>
/// <param name="Name">Its guarded name, or <see langword="null" /> where it has none.</param>
/// <param name="Carried">Whether the question the turn answers carries it.</param>
/// <param name="Shown">For a carried picture, whether it was shown with this turn.</param>
/// <param name="WholeText">For a carried short document, its words cut at the places they cross; otherwise <see langword="null" />.</param>
/// <param name="WholeTextCut">Whether the run's ceiling or the guard cut those words short.</param>
internal sealed record GuardedConversationFile(
    AgentConversationFile File,
    string? Name,
    bool Carried,
    bool Shown,
    IReadOnlyList<GuardedAttachmentPart>? WholeText,
    bool WholeTextCut);

/// <summary>One passage a search of a conversation's files found, its name and words already guarded.</summary>
/// <param name="File">The file it stands in.</param>
/// <param name="Name">The file's guarded name, or <see langword="null" /> where it has none.</param>
/// <param name="Segment">The page, slide, or sheet it lies on, or <see langword="null" />.</param>
/// <param name="Start">Where it begins in the file's words.</param>
/// <param name="End">Where it ends, exclusive.</param>
/// <param name="FoundBy">How the search reached it.</param>
/// <param name="Text">Its guarded words.</param>
/// <param name="Cut">Whether the run's ceiling or the guard cut them short.</param>
internal sealed record GuardedFilePassage(
    AgentConversationFileId File,
    string? Name,
    AttachmentTextSegment? Segment,
    int Start,
    int End,
    AgentConversationFileFoundBy FoundBy,
    string Text,
    bool Cut);
