// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using System.Globalization;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.AgentConversations;

/// <summary>The two tools a run is offered over the files a person handed to the conversation: a search of them by words and by meaning, and a reading of one a page at a time.</summary>
/// <remarks>
/// <para>
/// Offered only where the conversation holds a document that yielded words, so a conversation without files sees no
/// tool it could not use. Both read this conversation's files of this person and nothing else: an id of another
/// conversation's file, or of somebody else's, reads as no file at all.
/// </para>
/// <para>
/// Everything they return is spent from the run's ceiling on retrieved text before it is guarded, exactly as a page of
/// mail is, and every part is guarded on its own. A page is cut where the guard's analyzed ceiling cut it, so the next
/// page begins at the first character the model has not seen.
/// </para>
/// </remarks>
internal sealed class AgentConversationFileTools
{
    /// <summary>Names the tool that searches the conversation's files.</summary>
    internal const string SearchFilesToolName = "search_files";

    /// <summary>Names the tool that reads one of the conversation's files.</summary>
    internal const string ReadFileToolName = "read_file";

    /// <summary>The most passages one search returns.</summary>
    internal const int MaximumSearchPassages = 8;

    /// <summary>What the model is told the search does.</summary>
    private const string SearchFilesDescription =
        "Searches the files the person handed to this conversation — the documents the turn lists — by the words of your "
        + "query and by its meaning, and returns at most 8 passages, each with the file's id, the page, slide, or sheet "
        + "it lies on, where it starts and ends, and found-by saying whether its words or its meaning matched. Use it "
        + "for a long document, whose words are not in the turn, to find the part that answers the question; then read "
        + "on from that place with " + ReadFileToolName + " where the passage alone does not settle it. by-meaning=\"false\" "
        + "says only words were searched. It searches no mail.";

    /// <summary>What the model is told the reading does.</summary>
    private static readonly string ReadFileDescription =
        $"Reads one file the person handed to this conversation, a page of at most {AgentConversationTools.MaximumPageCharacters} "
        + "characters at a time, each part under the page, slide, or sheet it came from. Name the file by the file-id the "
        + "turn or a search showed, and either a page, slide, or sheet number to start there or the next attribute of an "
        + "earlier page to read on; omit both to start at the beginning. The page says how many characters the whole "
        + "holds. A picture is not read here: it was shown with the question that carried it. A file nothing could read "
        + "answers with the reason instead.";

    private readonly AgentAnswerJournal journal;
    private readonly AgentConversationFiles files;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly MailAnsweringRunLedger runLedger;
    private readonly AgentQuestion question;
    private readonly AgentTurnFiles turnFiles;
    private readonly List<string> shown = [];

    /// <summary>Initializes the tools for one run.</summary>
    /// <param name="journal">Reports what the run is doing, and stops it where the person stopped it.</param>
    /// <param name="files">The conversations' files.</param>
    /// <param name="egressGuard">Guards everything the tools return.</param>
    /// <param name="runLedger">Spends what they return from the run's ceiling.</param>
    /// <param name="question">The question, which names the conversation and its person.</param>
    /// <param name="turnFiles">What the turn was given of the files, which names the ones the conversation holds.</param>
    internal AgentConversationFileTools(
        AgentAnswerJournal journal,
        AgentConversationFiles files,
        SensitiveContentEgressGuard egressGuard,
        MailAnsweringRunLedger runLedger,
        AgentQuestion question,
        AgentTurnFiles turnFiles)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(runLedger);
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(turnFiles);

        this.journal = journal;
        this.files = files;
        this.egressGuard = egressGuard;
        this.runLedger = runLedger;
        this.question = question;
        this.turnFiles = turnFiles;
        this.shown.AddRange(turnFiles.ShownWords);
    }

    /// <summary>Gets every word of the person's files the run has shown the model, which an answer's links are traced to.</summary>
    internal IEnumerable<string> Shown => [.. this.shown];

    /// <summary>Creates the tools, or none where the conversation holds no document with words to search or read.</summary>
    /// <returns>The tools.</returns>
    internal IReadOnlyList<AIFunction> Create() =>
        this.turnFiles.Held.Any(static file => file.Reading is { WasRead: true })
            ?
            [
                AIFunctionFactory.Create(this.SearchFilesAsync, SearchFilesToolName, SearchFilesDescription),
                AIFunctionFactory.Create(this.ReadFileAsync, ReadFileToolName, ReadFileDescription),
            ]
            : [];

    [Description("Searches the conversation's files.")]
    private async Task<string> SearchFilesAsync(
        [Description("What to look for, in the words a passage that answers would use.")] string query,
        CancellationToken cancellationToken = default)
    {
        await this.ReportAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(query))
        {
            return "A search names what to look for.";
        }

        // The same ceiling a mail search is held to: the query is model-written, reaches the embedding provider whole,
        // and is scanned term by term against every passage of every file.
        if (query.Length > EmailSearchQueryText.MaximumLength)
        {
            return string.Create(CultureInfo.InvariantCulture, $"A search is at most {EmailSearchQueryText.MaximumLength} characters; ask again in the few words a passage that answers would use.");
        }

        var found = await this.files.SearchAsync(this.question.Conversation, this.question.User, query, MaximumSearchPassages, cancellationToken);
        List<GuardedFilePassage> passages = [];

        foreach (var finding in found.Findings)
        {
            var granted = this.runLedger.AdmitFileText(finding.Text.Length);

            if (granted is 0)
            {
                break;
            }

            var window = MailTextBounds.TruncateAtTextElementBoundary(finding.Text, granted);

            if (window.Length is 0)
            {
                break;
            }

            var guarded = await this.egressGuard.GuardWithOmissionAsync(SensitiveContentEgressPoint.ChatPrompt, window, cancellationToken);
            var name = await this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, finding.FileName, cancellationToken);

            this.shown.Add(window[..(window.Length - guarded.OmittedCharacterCount)]);
            passages.Add(new GuardedFilePassage(
                finding.Passage.File,
                name,
                finding.Passage.Segment,
                finding.Passage.StartOffset,
                finding.Passage.StartOffset + window.Length - guarded.OmittedCharacterCount,
                finding.FoundBy,
                guarded.Text,
                window.Length < finding.Text.Length || guarded.WasCutAtAnalyzedCeiling));
        }

        return passages.Count is 0 && found.Findings.Count > 0
            ? "This run may read no more: its ceiling on retrieved text is spent. Answer from what you have read, and say how much of the files that covers."
            : AgentConversationFileReadings.FileSearch(found.SearchedByMeaning, passages);
    }

    [Description("Reads one of the conversation's files.")]
    private async Task<string> ReadFileAsync(
        [Description("The file-id of the file, as the turn or a search showed it.")] string fileId,
        [Description("The page, slide, or sheet number to start at, counted from 1. Omit to start at the beginning or to continue with from.")] int? page = null,
        [Description("Where to continue, in characters from the beginning of the file's words: the next attribute of the page before. Omit to start at the beginning or at the page you named.")] int? from = null,
        CancellationToken cancellationToken = default)
    {
        await this.ReportAsync(cancellationToken);

        if (!Guid.TryParse(fileId, out var parsed) || parsed == Guid.Empty
            || this.turnFiles.Held.FirstOrDefault(file => file.Id.Value == parsed) is not { } file)
        {
            return "No file with that id is held by this conversation; the turn lists every one it holds.";
        }

        var name = await this.egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, file.Name, cancellationToken);

        if (file.Reading is not { WasRead: true }
            || await this.files.ReadTextAsync(this.question.Conversation, this.question.User, file.Id, cancellationToken) is not { } document)
        {
            return AgentConversationFileReadings.FileUnread(file, name);
        }

        if (StartOfPlace(document, page, from) is not { } start)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The file was read into {file.Reading.PageCount} places numbered from 1; name one of them, or leave the page out.");
        }

        if (start >= document.Text.Length)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The file's words hold {document.Text.Length} characters, so nothing remains to read from {start}.");
        }

        var whole = MailTextBounds.TruncateAtTextElementBoundary(document.Text[start..], AgentConversationTools.MaximumPageCharacters);
        var granted = this.runLedger.AdmitFileText(whole.Length);

        if (granted is 0)
        {
            return "This run may read no more: its ceiling on retrieved text is spent. Answer from what you have read, and say how much of the file that covers.";
        }

        var window = granted < whole.Length ? MailTextBounds.TruncateAtTextElementBoundary(whole, granted) : whole;

        // What is left of the ceiling can be narrower than the first text element, and an empty page would point its
        // next at the place it started from.
        if (window.Length is 0)
        {
            return "This run may read no more: its ceiling on retrieved text is spent. Answer from what you have read, and say how much of the file that covers.";
        }
        var (parts, end) = await AgentConversationTools.GuardedPartsAsync(
            this.egressGuard,
            document.Text,
            document.Segments,
            start,
            window.Length,
            cancellationToken);

        this.shown.Add(document.Text[start..end]);

        return AgentConversationFileReadings.FilePage(
            file,
            name,
            new PageBounds(document.Text.Length, start, end, granted < whole.Length),
            parts);
    }

    /// <summary>Reads where a page begins: at the place named, at a character the model was handed, or at the beginning.</summary>
    private static int? StartOfPlace(AgentConversationFileText document, int? place, int? from) =>
        from is not null || place is null
            ? AgentConversationTools.StartWithin(document.Text, from)
            : document.Segments.FirstOrDefault(segment => segment.Number == place)?.StartOffset;

    private async Task ReportAsync(CancellationToken cancellationToken)
    {
        if (!await this.journal.ReportAsync(AgentActivity.ReadingFiles, cancellationToken))
        {
            this.journal.Stopping.ThrowIfCancellationRequested();
        }
    }
}
