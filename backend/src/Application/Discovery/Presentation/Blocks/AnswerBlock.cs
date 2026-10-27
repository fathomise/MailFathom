// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Answers;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>One synthesized answer, in the words the run wrote it in.</summary>
/// <remarks>
/// The block for a question that has an answer rather than a shape — "did they ever agree to it", "what was the figure".
/// It is prose, so it is the block most able to say something the correspondence does not: the confidence beside it
/// says how much of it the sources settle, and the evidence beside that says whether they settle anything at all.
/// <para>
/// The text is Markdown, and the links it writes are judged by the service rather than by whoever draws it: each carries
/// the judgement a link in mail carries and whether the mail the answer cites carries it at all, so two clients cannot
/// come to disagree about which of an answer's links deserves a warning. The text carries no picture: a composer
/// neutralises its answer before it cuts and judges it, and the block applies the same
/// <see cref="AnswerMarkup.WithoutPictures" /> again itself, so a block read back from a revision written before the rule
/// existed holds to it too. Applying it to a text already neutralised changes nothing.
/// </para>
/// </remarks>
public sealed record AnswerBlock : PresentationBlock
{
    /// <summary>Initializes one synthesized answer.</summary>
    /// <param name="evidence">What the correspondence does for the answer.</param>
    /// <param name="text">The answer.</param>
    /// <param name="confidence">How far the answer is worth trusting beyond the sources it names.</param>
    /// <param name="links">Every link the text writes, judged; none where it writes none, and none on a block composed before links were judged.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text" /> is the unspecified default, or <paramref name="links" /> holds a null entry or more than <see cref="AnswerMarkup.MaximumLinks" />.</exception>
    public AnswerBlock(
        PresentationEvidence evidence,
        PresentationText text,
        PresentationConfidence confidence,
        IReadOnlyList<AnswerLink>? links = null)
        : base(PresentationBlockType.Answer, evidence)
    {
        PresentationRequirement.Specified(text, nameof(text));

        this.Text = WithoutPictures(text);
        this.Confidence = confidence;
        this.Links = PresentationRequirement.OptionalItems(links ?? [], AnswerMarkup.MaximumLinks, nameof(links));
    }

    /// <summary>Gets the answer.</summary>
    public PresentationText Text { get; }

    /// <summary>Gets how far the answer is worth trusting beyond the sources it names.</summary>
    public PresentationConfidence Confidence { get; }

    /// <summary>Gets every link the text writes, each judged and each saying whether the cited mail carries it.</summary>
    public IReadOnlyList<AnswerLink> Links { get; }

    /// <summary>Neutralises a text's pictures, cutting what neutralising added past what a block may carry.</summary>
    private static PresentationText WithoutPictures(PresentationText text)
    {
        var neutralised = AnswerMarkup.WithoutPictures(text.Value);

        return neutralised == text.Value
            ? text
            : PresentationText.Create(MailTextBounds.TruncateAtTextElementBoundary(neutralised, PresentationText.MaxLength));
    }
}
