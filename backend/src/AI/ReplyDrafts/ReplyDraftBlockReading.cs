// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Frozen;
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

    /// <summary>The palette a person picks a colour from, by the name each swatch is offered under.</summary>
    /// <remarks>ADR 0047 Question 2 states the values; each reaches 4.5:1 against the white page a message is read on.</remarks>
    private static readonly FrozenDictionary<string, MailDocumentColour> Swatches = new Dictionary<string, MailDocumentColour>(StringComparer.OrdinalIgnoreCase)
    {
        ["Red"] = new(0xb4, 0x23, 0x18),
        ["Orange"] = new(0xb5, 0x47, 0x08),
        ["Amber"] = new(0x8a, 0x61, 0x00),
        ["Green"] = new(0x06, 0x76, 0x47),
        ["Teal"] = new(0x0e, 0x74, 0x90),
        ["Blue"] = new(0x17, 0x5c, 0xd3),
        ["Purple"] = new(0x69, 0x41, 0xc6),
        ["Pink"] = new(0xc1, 0x15, 0x74),
        ["Grey"] = new(0x66, 0x70, 0x85),
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

        IReadOnlyList<MailDocumentBlock> worded = [.. blocks.Where(static block => block is not MailParagraphBlock paragraph || paragraph.Content.Count > 0)];

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

    /// <summary>One reading, holding the links it resolves against and what is left of the words it may keep.</summary>
    private sealed class Reading(IReadOnlyList<ReplyDraftLink> links)
    {
        private int remainingCharacters = ReplyDraft.MaximumBodyLength;

        internal bool IsExhausted => this.remainingCharacters <= 0;

        internal IEnumerable<MailDocumentBlock> BlocksOf(JsonElement block)
        {
            if (this.IsExhausted)
            {
                return [];
            }

            if (block.ValueKind is JsonValueKind.String)
            {
                return this.ParagraphsOf(block.GetString()!);
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
        internal IEnumerable<MailDocumentBlock> ParagraphsOf(string text) =>
        [
            .. Normalized(text)
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(paragraph => new MailParagraphBlock(this.PlainRun(paragraph), MailBlockAlignment.Inherited)),
        ];

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

        private MailListBlock ListOf(JsonElement block) => new(
            IsTrue(block, "ordered"),
            [
                .. (block.TryGetProperty("items", out var items) && items.ValueKind is JsonValueKind.Array
                        ? items.EnumerateArray().Take(MaximumListItems)
                        : [])
                    .Select(this.RunsOf)
                    .ToArray()
                    .Where(static runs => runs.Count > 0)
                    .Select(static runs => new MailListItem([new MailParagraphBlock(runs, MailBlockAlignment.Inherited)])),
            ]);

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

            return
            [
                new MailTableBlock(
                    [.. Enumerable.Repeat(new MailTableColumn(WidthShare: null), columns)],
                    [
                        .. rows.Select((row, index) => new MailTableRow(
                            header && index is 0,
                            [
                                .. row.Select(this.CellOf),
                                .. Enumerable.Repeat(EmptyCell(), columns - row.Length),
                            ])),
                    ]),
            ];
        }

        private static MailTableCell EmptyCell() => new(1, 1, MailBlockAlignment.Inherited, Background: null, []);

        private MailTableCell CellOf(JsonElement cell)
        {
            var runs = this.RunsOf(cell);

            return new MailTableCell(
                1,
                1,
                AlignmentOf(cell),
                Background: null,
                runs.Count is 0 ? [] : [new MailParagraphBlock(runs, MailBlockAlignment.Inherited)]);
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

        private MailParagraphBlock WordsAsParagraph(JsonElement[] row) =>
            new([.. row.SelectMany(cell => this.WordsOf(cell)).Take(MaximumRuns)], MailBlockAlignment.Inherited);

        /// <summary>Reads a shape this does not take as the plain words inside it, a space between each.</summary>
        private IReadOnlyList<MailInlineRun> WordsOf(JsonElement element)
        {
            var words = new List<string>();

            CollectWords(element, 0, words);

            return this.PlainRun(string.Join(' ', words.Where(static word => !string.IsNullOrWhiteSpace(word))));
        }

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

            if (this.IsExhausted || string.IsNullOrWhiteSpace(normalized))
            {
                return [];
            }

            var kept = MailTextBounds.TruncateAtTextElementBoundary(normalized, this.remainingCharacters);

            this.remainingCharacters = kept.Length < normalized.Length ? 0 : this.remainingCharacters - kept.Length;

            return [new MailInlineRun(kept, emphasis, foreground, link)];
        }
    }
}
