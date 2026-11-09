// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Frozen;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Recognizes the block of header lines another mail client typed into a forward or a quoted reply.</summary>
/// <remarks>
/// <para>
/// Mail has no mark for this block, so recognition is deliberately narrow and leans one way: whatever does not match
/// stays on the screen. A wrong miss costs a reader a few lines, and a wrong fold costs them words they never saw, which
/// is why any doubt about a run refuses the whole of it rather than folding the part that looked right.
/// </para>
/// <para>
/// The labels and separators are the closed set Gmail, Outlook, and Apple Mail write in English, Polish, and German. They
/// are compared without regard to case, and one block may mix languages because Gmail localizes only some of its labels.
/// </para>
/// </remarks>
internal static class MailWrittenHeaderBlocks
{
    /// <summary>The longest line a block may hold, which is room for a long recipient list and no room for prose.</summary>
    private const int MaximumLineLength = 1024;

    /// <summary>The longest text read as a label, past which a colon is punctuation in a sentence.</summary>
    private const int MaximumLabelLength = 32;

    /// <summary>The most consecutive paragraphs a run is read from: one per role, a separator, and the one under them that can refuse it.</summary>
    private const int MaximumBlocksInOneRun = 11;

    private const int ShortestUnderscoreSeparator = 8;

    private static readonly FrozenDictionary<string, HeaderRole> Labels = new Dictionary<string, HeaderRole>
    {
        ["From"] = HeaderRole.Author,
        ["Od"] = HeaderRole.Author,
        ["Von"] = HeaderRole.Author,
        ["Date"] = HeaderRole.Date,
        ["Sent"] = HeaderRole.Date,
        ["Data"] = HeaderRole.Date,
        ["Wysłano"] = HeaderRole.Date,
        ["Wysłany"] = HeaderRole.Date,
        ["Datum"] = HeaderRole.Date,
        ["Gesendet"] = HeaderRole.Date,
        ["To"] = HeaderRole.Recipient,
        ["Do"] = HeaderRole.Recipient,
        ["An"] = HeaderRole.Recipient,
        ["Cc"] = HeaderRole.Copy,
        ["DW"] = HeaderRole.Copy,
        ["Kopia"] = HeaderRole.Copy,
        ["Bcc"] = HeaderRole.BlindCopy,
        ["UDW"] = HeaderRole.BlindCopy,
        ["Subject"] = HeaderRole.Subject,
        ["Temat"] = HeaderRole.Subject,
        ["Betreff"] = HeaderRole.Subject,
        ["Reply-To"] = HeaderRole.ReplyTo,
        ["Odpowiedź do"] = HeaderRole.ReplyTo,
        ["Antwort an"] = HeaderRole.ReplyTo,
        ["Importance"] = HeaderRole.Importance,
        ["Ważność"] = HeaderRole.Importance,
        ["Priorytet"] = HeaderRole.Importance,
        ["Wichtigkeit"] = HeaderRole.Importance,
        ["Attachments"] = HeaderRole.Attachments,
        ["Załączniki"] = HeaderRole.Attachments,
        ["Anlagen"] = HeaderRole.Attachments,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> DashedSeparators = new[]
    {
        "Forwarded message",
        "Original Message",
        "Przekazana wiadomość",
        "Wiadomość przekazana dalej",
        "Oryginalna wiadomość",
        "Wiadomość oryginalna",
        "Weitergeleitete Nachricht",
        "Ursprüngliche Nachricht",
        "Original-Nachricht",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> IntroducingSeparators = new[]
    {
        "Begin forwarded message:",
        "Początek przekazywanej wiadomości:",
        "Początek przekazanej wiadomości:",
        "Anfang der weitergeleiteten Nachricht:",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private enum HeaderRole
    {
        Author = 0,
        Date = 1,
        Recipient = 2,
        Copy = 3,
        BlindCopy = 4,
        Subject = 5,
        ReplyTo = 6,
        Importance = 7,
        Attachments = 8,
    }

    /// <summary>Names the blocks of header lines among one list of sibling blocks, which keeps every block and every word.</summary>
    /// <param name="blocks">The blocks of one level of the document, or of one quotation.</param>
    /// <returns>The same blocks, the first of each recognized block of header lines naming it.</returns>
    internal static IReadOnlyList<MailDocumentBlock> Marked(IReadOnlyList<MailDocumentBlock> blocks)
    {
        List<MailDocumentBlock>? marked = null;
        var at = 0;

        while (at < blocks.Count)
        {
            if (ReadBlocksAt(blocks, at) is not { } headers)
            {
                at++;

                continue;
            }

            marked ??= [.. blocks];
            marked[at] = blocks[at] with { WrittenHeaders = headers };
            at += headers.BlockCount;
        }

        return marked ?? blocks;
    }

    /// <summary>Cuts a plain-text paragraph either side of a block of header lines, so the block is a paragraph of its own.</summary>
    /// <remarks>
    /// A quoted message arrives with a marker on every line, its blank lines included, so its header lines and the words
    /// under them are one paragraph and folding that paragraph would take the words with it. Nothing else is cut, and the
    /// lines keep their markers, so what a reader unfolds is what the message wrote.
    /// </remarks>
    /// <param name="paragraph">One paragraph of a plain-text body.</param>
    /// <returns>The paragraph, or the pieces of it in order, none of them blank.</returns>
    internal static IEnumerable<string> SeparatedFromTheWordsAround(string paragraph)
    {
        var lines = paragraph.Split('\n');
        var pieces = new List<string>();
        var pieceStart = 0;
        var at = 0;

        while (at < lines.Length)
        {
            if (ReadRun(lines, at) is not { } run)
            {
                at++;

                continue;
            }

            pieces.Add(string.Join('\n', lines[pieceStart..at]));
            pieces.Add(string.Join('\n', lines[at..(at + run.LineCount)]));
            at += run.LineCount;
            pieceStart = at;
        }

        pieces.Add(string.Join('\n', lines[pieceStart..]));

        return pieces.Where(static piece => piece.Trim().Length > 0);
    }

    private static MailWrittenHeaders? ReadBlocksAt(IReadOnlyList<MailDocumentBlock> blocks, int start)
    {
        var rule = blocks[start] is MailSeparatorBlock ? 1 : 0;

        if (start + rule >= blocks.Count
            || blocks[start + rule] is not MailParagraphBlock first
            || !OpensARun(LinesOf(first)))
        {
            return null;
        }

        var paragraphs = blocks
            .Skip(start + rule)
            .Take(MaximumBlocksInOneRun)
            .TakeWhile(static block => block is MailParagraphBlock)
            .Select(static block => LinesOf((MailParagraphBlock)block))
            .ToArray();

        if (ReadRun([.. paragraphs.SelectMany(static lines => lines)], 0) is not { } run)
        {
            return null;
        }

        var paragraphCount = ParagraphsCovering(paragraphs, run.LineCount);

        return paragraphCount == 0 ? null : new MailWrittenHeaders(rule + paragraphCount, run.Author, run.Date);
    }

    /// <summary>Counts the paragraphs a run covers exactly, or answers zero where it ends inside one.</summary>
    /// <remarks>Folding a paragraph the run only partly covers would hide the words sharing it.</remarks>
    private static int ParagraphsCovering(string[][] paragraphs, int lineCount)
    {
        var covered = 0;

        for (var paragraph = 0; paragraph < paragraphs.Length; paragraph++)
        {
            covered += paragraphs[paragraph].Length;

            if (covered >= lineCount)
            {
                return covered == lineCount ? paragraph + 1 : 0;
            }
        }

        return 0;
    }

    private static bool OpensARun(string[] lines) =>
        lines.Length > 0 && (IsSeparator(lines[0]) || ReadField(lines[0]) is not null);

    private static string[] LinesOf(MailParagraphBlock paragraph) =>
    [
        .. string.Concat(paragraph.Content.Select(static run => run.Text))
            .Split('\n')
            .Where(static line => Unquoted(line).Length > 0),
    ];

    /// <summary>Reads one run of header lines starting at a line, the optional separator above them included.</summary>
    /// <remarks>
    /// A role written twice ends the run where it repeats, and a label under the run refuses it: an unknown label says the
    /// block is one this list does not know, so the part already read is not known to be the whole of it. That refuses a
    /// body opening with a word and a colon too, which the direction of this class accepts.
    /// </remarks>
    private static (int LineCount, string Author, string Date)? ReadRun(string[] lines, int start)
    {
        var at = start < lines.Length && IsSeparator(lines[start]) ? start + 1 : start;
        var fields = new Dictionary<HeaderRole, string>();

        while (at < lines.Length && ReadField(lines[at]) is { } field && fields.TryAdd(field.Role, field.Value))
        {
            at++;
        }

        var complete = fields.ContainsKey(HeaderRole.Author)
            && fields.ContainsKey(HeaderRole.Date)
            && (fields.ContainsKey(HeaderRole.Recipient) || fields.ContainsKey(HeaderRole.Subject));

        return complete && !(at < lines.Length && ReadLabel(lines[at]) is not null)
            ? (at - start, AuthorNameIn(fields[HeaderRole.Author]), fields[HeaderRole.Date])
            : null;
    }

    private static (HeaderRole Role, string Value)? ReadField(string line) =>
        line.Length <= MaximumLineLength
        && ReadLabel(line) is { } labelled
        && Labels.TryGetValue(labelled.Label, out var role)
            ? (role, labelled.Value)
            : null;

    /// <summary>Splits a line at its first colon where the text before it could be a label and a value follows it.</summary>
    private static (string Label, string Value)? ReadLabel(string line)
    {
        var text = Unquoted(line);
        var colon = text.IndexOf(':', StringComparison.Ordinal);

        if (colon <= 0 || colon > MaximumLabelLength)
        {
            return null;
        }

        var label = text[..colon].TrimEnd();
        var value = text[(colon + 1)..].Trim();

        return value.Length > 0 && label.All(static character => char.IsLetter(character) || character is '-' or ' ')
            ? (label, value)
            : null;
    }

    private static bool IsSeparator(string line)
    {
        var text = Unquoted(line);

        if (text.Length >= ShortestUnderscoreSeparator && text.All(static character => character == '_'))
        {
            return true;
        }

        return text.StartsWith("--", StringComparison.Ordinal) && text.EndsWith("--", StringComparison.Ordinal)
            ? DashedSeparators.Contains(text.Trim('-').Trim())
            : IntroducingSeparators.Contains(text);
    }

    /// <summary>Reads a line without the quotation markers a plain-text body carries in front of it.</summary>
    private static string Unquoted(string line) => line.TrimStart(' ', '\t', '>', ' ').Trim();

    /// <summary>Reads the name out of an author line, leaving the address only where it is all the line says.</summary>
    private static string AuthorNameIn(string value)
    {
        var addressStart = value.AsSpan().IndexOfAny('<', '[');
        var name = addressStart > 0 ? value[..addressStart].Trim().Trim('"', '\'').Trim() : string.Empty;

        return name.Length > 0 ? name : value.Trim('<', '>', '[', ']', ' ');
    }
}
