// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>States which passages of a draft somebody asked to have rewritten, and how.</summary>
/// <remarks>
/// <para>
/// The draft travels as its own plain text, paragraphs separated by line breaks, and that text is the whole of what a
/// rewriting is grounded in: no message is named and nothing is read out of a mailbox. The passages are the whole
/// sentences a selection touched, one stretch per paragraph, so each one can be rewritten in place and read back between
/// the words before and after it.
/// </para>
/// <para>
/// Every invariant is held here rather than by whoever built the request, so a request that exists is one a rewriting can
/// answer: a draft that is not blank and not past its bound, at least one and at most <see cref="MaximumPassages" />
/// passages, each holding words, each inside the draft, in the order they stand in it and none overlapping another, and
/// an ask that is not blank and not past its bound. <see cref="RefusalOf" /> says which of them a candidate breaks, so a
/// boundary refuses in the words of the rule rather than restating it.
/// </para>
/// </remarks>
public sealed record PassageRewriteRequest
{
    /// <summary>The greatest length a draft sent for rewriting may have.</summary>
    /// <remarks>The bound a drafted body is held to, because the text being rewritten is a reply somebody edits, and a drafted one is never longer.</remarks>
    public const int MaximumTextLength = ReplyDraft.MaximumBodyLength;

    /// <summary>The greatest number of passages one rewriting may name.</summary>
    /// <remarks>
    /// One passage per paragraph a selection touches, so this is a selection spanning that many paragraphs. Past it the
    /// person has selected most of a long draft, and asking for the whole reply again is the better act.
    /// </remarks>
    public const int MaximumPassages = 16;

    /// <summary>Initializes a request, refusing one that breaks any invariant.</summary>
    /// <param name="text">The draft as plain text, paragraphs separated by line breaks.</param>
    /// <param name="passages">The stretches of it to rewrite, in the order they stand in it.</param>
    /// <param name="instruction">What the person asked to change about them.</param>
    /// <exception cref="ArgumentException">Thrown when the request breaks an invariant, the message naming which.</exception>
    public PassageRewriteRequest(string text, IReadOnlyList<DraftPassage> passages, string instruction)
    {
        if (RefusalOf(text, passages, instruction) is { } refusal)
        {
            throw new ArgumentException(refusal);
        }

        this.Text = text;
        this.Passages = [.. passages];
        this.Instruction = instruction;
    }

    /// <summary>Gets the draft as plain text, paragraphs separated by line breaks.</summary>
    public string Text { get; }

    /// <summary>Gets the stretches of the draft to rewrite, in the order they stand in it, none overlapping another.</summary>
    public IReadOnlyList<DraftPassage> Passages { get; }

    /// <summary>Gets what the person asked to change about the passages: one of the composer's own asks, or what they typed.</summary>
    public string Instruction { get; }

    /// <summary>Names what a candidate request breaks, without quoting any of it.</summary>
    /// <param name="text">The draft.</param>
    /// <param name="passages">The stretches of it to rewrite.</param>
    /// <param name="instruction">What the person asked to change about them.</param>
    /// <returns>A sentence stating the rule the candidate breaks, or <see langword="null" /> where it breaks none.</returns>
    /// <remarks>The sentence names a bound and never a word of the draft, because what somebody is writing is as private as the mail it answers and a refusal is what reaches a log.</remarks>
    public static string? RefusalOf(string? text, IReadOnlyList<DraftPassage>? passages, string? instruction)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "A rewriting carries the draft its passages stand in.";
        }

        if (text.Length > MaximumTextLength)
        {
            return $"A draft sent for rewriting is at most {MaximumTextLength} characters, which is the reply somebody edits.";
        }

        if (passages is null || passages.Count is 0)
        {
            return "A rewriting names at least one passage of the draft to rewrite.";
        }

        if (passages.Count > MaximumPassages)
        {
            return $"A rewriting names at most {MaximumPassages} passages, one for each paragraph a selection touches.";
        }

        if (PassageRefusalOf(text, passages) is { } passageRefusal)
        {
            return passageRefusal;
        }

        if (string.IsNullOrWhiteSpace(instruction))
        {
            return "A rewriting says how the passages should change.";
        }

        return instruction.Length > ReplyDraftRequest.MaximumInstructionLength
            ? $"An instruction is at most {ReplyDraftRequest.MaximumInstructionLength} characters, which is a sentence or two saying how the passages should change."
            : null;
    }

    /// <summary>Names what the passages break, read one after another so each is held against the one before it.</summary>
    private static string? PassageRefusalOf(string text, IReadOnlyList<DraftPassage> passages)
    {
        var previousEnd = 0;

        foreach (var passage in passages)
        {
            if (passage.Start < 0 || passage.Length <= 0)
            {
                return "A passage is a stretch of the draft that holds at least one character.";
            }

            // Widened so a start and a length near the integer's ceiling cannot wrap round into a stretch inside the text.
            if ((long)passage.Start + passage.Length > text.Length)
            {
                return "A passage lies inside the draft it is named in.";
            }

            if (passage.Start < previousEnd)
            {
                return "Passages are named in the order they stand in the draft, and none overlaps another.";
            }

            if (string.IsNullOrWhiteSpace(text.AsSpan(passage.Start, passage.Length).ToString()))
            {
                return "A passage holds words to rewrite.";
            }

            previousEnd = passage.Start + passage.Length;
        }

        return null;
    }
}
