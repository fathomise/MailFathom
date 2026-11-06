// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Domain.Emails;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>Reads the blocks a reply-drafting agent wrote into the composer's closed set, and nothing outside it.</summary>
/// <remarks>
/// <para>
/// What a model wrote is text a message being answered can steer, so this is a schema check rather than a parser: a
/// value is read only where it sits in a shape the instruction names, and whatever does not fit is dropped to its words
/// rather than refused. A paragraph, a one-level list, and a table are the blocks; bold, italic, a link, and a colour
/// are what a run carries; an alignment is what a paragraph and a cell carry. A heading, a picture, a size, a nested
/// list, and a table inside a table are none of them, so each is its words.
/// </para>
/// <para>
/// A link is a number in the list the turn published and nothing else. A number the turn never published, and an
/// address the model typed in its place, both leave the words as words, which is what makes a link to an address from
/// outside the exchange impossible rather than unlikely. A colour is a palette swatch's name, never a value, so a model
/// cannot write a colour nobody can read.
/// </para>
/// <para>
/// The words are bounded as the plain draft always was, at <see cref="ReplyDraft.MaximumBodyLength" />: the run that
/// reaches the bound is cut there and nothing after it is read. A table is held to 200 rows and 20 columns, and one
/// past either is read as its rows' words instead, because a figure silently missing from a table is worse than a table
/// drawn as lines.
/// </para>
/// </remarks>
internal static class ReplyDraftBlockReading
{
    /// <summary>The most blocks one draft holds.</summary>
    internal const int MaximumBlocks = 100;

    /// <summary>The most items one list holds.</summary>
    internal const int MaximumListItems = 100;

    /// <summary>The most rows one table holds, as the composer's own tables are bounded.</summary>
    internal const int MaximumTableRows = 200;

    /// <summary>The most columns one table holds, as the composer's own tables are bounded.</summary>
    internal const int MaximumTableColumns = 20;

    /// <summary>The most runs one paragraph, item, or cell holds.</summary>
    internal const int MaximumRuns = 200;

    /// <summary>How deep words are looked for inside a shape this does not read.</summary>
    private const int MaximumWordsDepth = 6;

    /// <summary>The most the plain reading writes around one list item: the longest number, its stop, and the line it ends.</summary>
    private static readonly int ListItemSeparatorsLength =
        MaximumListItems.ToString(CultureInfo.InvariantCulture).Length + ". ".Length + ReplyDraft.LineBreak.Length;

    /// <summary>The colours a draft may write, by the name of the hue each is offered under.</summary>
    /// <remarks>
    /// Each is the vivid shade of its hue in the composer's palette, so a coloured run the agent writes is one a person
    /// could have picked, and reads on the light and the dark theme alike as ADR 0047 Question 2 requires.
    /// </remarks>
    private static readonly FrozenDictionary<string, MailDocumentColour> Swatches = new Dictionary<string, MailDocumentColour>(StringComparer.OrdinalIgnoreCase)
    {
        ["Red"] = new(0xf7, 0x30, 0x30),
        ["Orange"] = new(0xca, 0x65, 0x20),
        ["Amber"] = new(0xa6, 0x7a, 0x21),
        ["Green"] = new(0x21, 0x94, 0x49),
        ["Teal"] = new(0x22, 0x90, 0x85),
        ["Blue"] = new(0x3e, 0x7c, 0xf5),
        ["Purple"] = new(0x9f, 0x5e, 0xf5),
        ["Pink"] = new(0xec, 0x2a, 0x9f),
        ["Grey"] = new(0x81, 0x81, 0x81),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, MailBlockAlignment> Alignments = new Dictionary<string, MailBlockAlignment>(StringComparer.OrdinalIgnoreCase)
    {
        ["start"] = MailBlockAlignment.Start,
        ["left"] = MailBlockAlignment.Start,
        ["center"] = MailBlockAlignment.Center,
        ["centre"] = MailBlockAlignment.Center,
        ["end"] = MailBlockAlignment.End,
        ["right"] = MailBlockAlignment.End,
        ["justify"] = MailBlockAlignment.Justify,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> BlockTypes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "paragraph", "list", "table");

    /// <summary>Gets the names of the swatches a draft may colour words with.</summary>
    internal static IEnumerable<string> SwatchNames => Swatches.Keys.Order(StringComparer.Ordinal);

    /// <summary>Reads what the model wrote as the draft's body.</summary>
    /// <param name="body">The body as the model wrote it: an array of blocks, or one plain text.</param>
    /// <param name="links">The links the turn published, in the order it numbered them.</param>
    /// <returns>The draft as a document, or <see langword="null" /> where it holds no words.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="links" /> is <see langword="null" />.</exception>
    /// <remarks>A body written as one string is a plain draft, read as paragraphs of plain runs between its blank lines.</remarks>
    internal static MailDocument? Read(JsonElement? body, IReadOnlyList<ReplyDraftLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        var reading = new Reading(links);
        IReadOnlyList<MailDocumentBlock> blocks = body switch
        {
            { ValueKind: JsonValueKind.Array } array => [.. array.EnumerateArray().Take(MaximumBlocks).SelectMany(reading.BlocksOf)],
            { ValueKind: JsonValueKind.String } text => [.. reading.ParagraphsOf(text.GetString()!)],
            _ => [],
        };

        IReadOnlyList<MailDocumentBlock> worded = [.. blocks.Where(HoldsWords)];

        return worded.Count is 0
            ? null
            : MailDocument.Reduced(
                worded,
                removedRemoteReferenceCount: 0,
                retainedRemoteImageCount: 0,
                inlineImageCount: 0,
                undrawnInlineImageCount: 0,
                truncated: reading.IsExhausted);
    }

    /// <summary>Whether a block holds a word somebody can edit, which an empty list, a table of empty cells, or a run of spaces does not.</summary>
    private static bool HoldsWords(MailDocumentBlock block) => block switch
    {
        MailParagraphBlock paragraph => paragraph.Content.Any(static run => !string.IsNullOrWhiteSpace(run.Text)),
        MailListBlock list => list.Items.Any(static item => item.Blocks.Any(HoldsWords)),
        MailTableBlock table => table.Rows.Any(static row => row.Cells.Any(static cell => cell.Blocks.Any(HoldsWords))),
        _ => false,
    };

    /// <summary>One reading, holding the links it resolves against and what is left of the words it may keep.</summary>
    /// <remarks>
    /// What is left is charged for the separators the draft's plain reading writes as well as for the words — a blank
    /// line ahead of each block, a marker and a line per list item, a line per table row, and a bar per cell — so the
    /// plain reading of everything kept fits the bound rather than being cut short beside a document that was not.
    /// </remarks>
    private sealed class Reading(IReadOnlyList<ReplyDraftLink> links)
    {
        private int remainingCharacters = ReplyDraft.MaximumBodyLength;

        internal bool IsExhausted => this.remainingCharacters <= 0;

        internal IEnumerable<MailDocumentBlock> BlocksOf(JsonElement block)
        {
            if (block.ValueKind is JsonValueKind.String)
            {
                return this.ParagraphsOf(block.GetString()!);
            }

            if (!this.Charged(ReplyDraft.BlockBreak.Length))
            {
                return [];
            }

            return TypeOf(block) switch
            {
                "paragraph" => [new MailParagraphBlock(this.RunsIn(block), AlignmentOf(block))],
                "list" => [this.ListOf(block)],
                "table" => this.TableOf(block),
                _ => [this.WordsAsParagraph(block)],
            };
        }

        /// <summary>Reads plain text as paragraphs between its blank lines, each one plain run.</summary>
        internal List<MailDocumentBlock> ParagraphsOf(string text)
        {
            var paragraphs = new List<MailDocumentBlock>();

            foreach (var paragraph in Normalized(text).Split(ReplyDraft.BlockBreak, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!this.Charged(ReplyDraft.BlockBreak.Length))
                {
                    break;
                }

                paragraphs.Add(new MailParagraphBlock(this.PlainRun(paragraph), MailBlockAlignment.Inherited));
            }

            return paragraphs;
        }

        private static string? TypeOf(JsonElement block) =>
            block.ValueKind is JsonValueKind.Object
            && block.TryGetProperty("type", out var type)
            && type.ValueKind is JsonValueKind.String
            && BlockTypes.TryGetValue(type.GetString()!, out var known)
                ? known
                : null;

        private static MailBlockAlignment AlignmentOf(JsonElement element) =>
            element.ValueKind is JsonValueKind.Object
            && element.TryGetProperty("align", out var align)
            && align.ValueKind is JsonValueKind.String
            && Alignments.TryGetValue(align.GetString()!, out var alignment)
                ? alignment
                : MailBlockAlignment.Inherited;

        private static bool IsTrue(JsonElement run, string name) =>
            run.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

        /// <summary>Keeps a model's line breaks as the newlines a run carries and turns every other control character into a space.</summary>
        private static string Normalized(string text)
        {
            var lines = text.ReplaceLineEndings("\n");

            return string.Create(lines.Length, lines, static (span, source) =>
            {
                for (var index = 0; index < source.Length; index++)
                {
                    span[index] = source[index] is not '\n' && char.IsControl(source[index]) ? ' ' : source[index];
                }
            });
        }

        /// <summary>The plain words inside a shape this does not take, a space between each.</summary>
        private static string WordsTextOf(JsonElement element)
        {
            var words = new List<string>();

            CollectWords(element, 0, words);

            return string.Join(' ', words.Where(static word => !string.IsNullOrWhiteSpace(word)));
        }

        private static void CollectWords(JsonElement element, int depth, List<string> words)
        {
            if (depth > MaximumWordsDepth)
            {
                return;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    words.Add(element.GetString()!);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        CollectWords(item, depth + 1, words);
                    }

                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject().Where(static property => property.Name is "text" or "runs" or "items" or "rows" or "cells" or "body"))
                    {
                        CollectWords(property.Value, depth + 1, words);
                    }

                    break;
                default:
                    break;
            }
        }

        private MailListBlock ListOf(JsonElement block)
        {
            var items = new List<MailListItem>();
            var written = block.TryGetProperty("items", out var listed) && listed.ValueKind is JsonValueKind.Array
                ? listed.EnumerateArray().Take(MaximumListItems)
                : [];

            foreach (var item in written)
            {
                if (!this.Charged(ListItemSeparatorsLength))
                {
                    break;
                }

                var runs = this.RunsOf(item);

                if (runs.Any(static run => !string.IsNullOrWhiteSpace(run.Text)))
                {
                    items.Add(new MailListItem([new MailParagraphBlock(runs, MailBlockAlignment.Inherited)]));
                }
            }

            return new MailListBlock(IsTrue(block, "ordered"), items);
        }

        private IEnumerable<MailDocumentBlock> TableOf(JsonElement block)
        {
            JsonElement[][] rows =
            [
                .. (block.TryGetProperty("rows", out var written) && written.ValueKind is JsonValueKind.Array
                        ? written.EnumerateArray()
                        : [])
                    .Select(static row => row.ValueKind is JsonValueKind.Array ? row.EnumerateArray().ToArray() : [row]),
            ];

            if (rows.Length is 0)
            {
                return [];
            }

            if (rows.Length > MaximumTableRows || rows.Any(static row => row.Length > MaximumTableColumns))
            {
                return [.. rows.Select(row => this.WordsAsParagraph(row))];
            }

            var columns = rows.Max(static row => row.Length);
            var header = IsTrue(block, "header");

            // A row is read only where its line and the cells it is padded with still fit, and the row the bound reached
            // inside its cells is dropped where it kept none of their words, rather than ending the table on bare cells.
            MailTableRow[] kept =
            [
                .. rows
                    .TakeWhile(row => this.Charged(ReplyDraft.LineBreak.Length + (ReplyDraft.CellBreak.Length * (columns - row.Length))))
                    .Select((row, index) => this.RowOf(row, header && index is 0, columns)),
            ];

            if (this.IsExhausted && kept.Length > 0 && !kept[^1].Cells.Any(static cell => cell.Blocks.Any(HoldsWords)))
            {
                kept = kept[..^1];
            }

            return [new MailTableBlock([.. Enumerable.Repeat(new MailTableColumn(WidthShare: null), columns)], kept)];
        }

        private MailTableRow RowOf(JsonElement[] row, bool isHeader, int columns)
        {

            return new MailTableRow(
                isHeader,
                [
                    .. row.Select(this.CellOf),
                    .. Enumerable.Repeat(EmptyCell(), columns - row.Length),
                ]);
        }

        private static MailTableCell EmptyCell() => new(1, 1, MailBlockAlignment.Inherited, Background: null, []);

        private MailTableCell CellOf(JsonElement cell)
        {
            if (!this.Charged(ReplyDraft.CellBreak.Length))
            {
                return EmptyCell();
            }

            var runs = this.RunsOf(cell);

            return new MailTableCell(
                1,
                1,
                AlignmentOf(cell),
                Background: null,
                runs.Any(static run => !string.IsNullOrWhiteSpace(run.Text)) ? [new MailParagraphBlock(runs, MailBlockAlignment.Inherited)] : []);
        }

        /// <summary>Reads the runs a paragraph holds, under the name the instruction gives them or as its words.</summary>
        private IReadOnlyList<MailInlineRun> RunsIn(JsonElement block) =>
            block.TryGetProperty("runs", out var runs) ? this.RunsOf(runs) : this.WordsOf(block);

        /// <summary>Reads runs out of whatever stands where runs are expected: one plain text, an array of runs, or an object holding them.</summary>
        private IReadOnlyList<MailInlineRun> RunsOf(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => this.PlainRun(element.GetString()!),
            JsonValueKind.Array => [.. element.EnumerateArray().Take(MaximumRuns).SelectMany(this.RunOf)],
            JsonValueKind.Object when element.TryGetProperty("runs", out var runs) => this.RunsOf(runs),
            JsonValueKind.Object => this.RunOf(element),
            _ => [],
        };

        private IReadOnlyList<MailInlineRun> RunOf(JsonElement run)
        {
            if (run.ValueKind is JsonValueKind.String)
            {
                return this.PlainRun(run.GetString()!);
            }

            if (run.ValueKind is not JsonValueKind.Object
                || TypeOf(run) is not null
                || !run.TryGetProperty("text", out var written)
                || written.ValueKind is not JsonValueKind.String)
            {
                return this.WordsOf(run);
            }

            var emphasis = (IsTrue(run, "bold") ? MailTextEmphasis.Bold : MailTextEmphasis.None)
                | (IsTrue(run, "italic") ? MailTextEmphasis.Italic : MailTextEmphasis.None);

            return this.Kept(written.GetString()!, emphasis, this.ColourOf(run), this.LinkOf(run));
        }

        private MailDocumentColour? ColourOf(JsonElement run) =>
            run.TryGetProperty("colour", out var colour) || run.TryGetProperty("color", out colour)
                ? colour.ValueKind is JsonValueKind.String && Swatches.TryGetValue(colour.GetString()!, out var swatch) ? swatch : null
                : null;

        private MailDocumentLink? LinkOf(JsonElement run) =>
            run.TryGetProperty("link", out var cited)
            && cited.ValueKind is JsonValueKind.Number
            && cited.TryGetInt32(out var position)
            && position >= 0
            && position < links.Count
                ? links[position].Link
                : null;

        private MailParagraphBlock WordsAsParagraph(JsonElement element) => new(this.WordsOf(element), MailBlockAlignment.Inherited);

        /// <summary>Reads one row of a table past its bounds as a paragraph of its cells' words, a bar between two cells as the plain reading writes one.</summary>
        private MailParagraphBlock WordsAsParagraph(JsonElement[] row) =>
            this.Charged(ReplyDraft.BlockBreak.Length)
                ? new(this.PlainRun(string.Join(ReplyDraft.CellBreak, row.Select(WordsTextOf).Where(static words => words.Length > 0))), MailBlockAlignment.Inherited)
                : new([], MailBlockAlignment.Inherited);

        /// <summary>Reads a shape this does not take as the plain words inside it, a space between each.</summary>
        private IReadOnlyList<MailInlineRun> WordsOf(JsonElement element) => this.PlainRun(WordsTextOf(element));

        private IReadOnlyList<MailInlineRun> PlainRun(string text) =>
            this.Kept(text, MailTextEmphasis.None, foreground: null, link: null);

        /// <summary>Keeps one run's words within what is left of the draft's bound, cutting the run that reaches it.</summary>
        private IReadOnlyList<MailInlineRun> Kept(
            string text,
            MailTextEmphasis emphasis,
            MailDocumentColour? foreground,
            MailDocumentLink? link)
        {
            var normalized = Normalized(text);

            // A run of spaces alone is kept, since it is what stands between the words of the runs either side of it.
            if (this.IsExhausted || normalized.Length is 0)
            {
                return [];
            }

            var kept = MailTextBounds.TruncateAtTextElementBoundary(normalized, this.remainingCharacters);

            this.remainingCharacters = kept.Length < normalized.Length ? 0 : this.remainingCharacters - kept.Length;

            return [new MailInlineRun(kept, emphasis, foreground, link)];
        }

        /// <summary>Charges what the plain reading writes beside the words, and says whether anything is left to read after it.</summary>
        private bool Charged(int characters)
        {
            this.Charge(characters);

            return !this.IsExhausted;
        }

        private void Charge(int characters) => this.remainingCharacters = Math.Max(0, this.remainingCharacters - characters);
    }
}
