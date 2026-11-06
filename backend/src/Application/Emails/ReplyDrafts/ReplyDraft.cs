// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>A reply somebody has not written yet: text they own, what it asserts, and who it proposes to reach.</summary>
/// <remarks>
/// <para>
/// <b>It is a local artifact and nothing else.</b> Nothing here is sent, written to a mail server, queued, or stored:
/// what comes back is text in the client, which its author edits, discards, or saves as a draft of their own through
/// the routes that already do that. Every irreversible act stays where it was — behind a person confirming it.
/// </para>
/// <para>
/// The proposed recipients are people the conversation itself names, resolved inside this deployment from positions a
/// producer answered with, so a draft can propose nobody the correspondence did not already carry. They are a proposal
/// in the strict sense: a client shows every one of them and adds none on its own.
/// </para>
/// <para>
/// <see cref="Nothing" /> is what a drafting that could not be made answers with, and it is not a failure a client
/// reports. A provider that was unreachable, an endpoint that answered unreadably, and a deployment that drafts nothing
/// all leave the composer exactly as somebody found it — empty, and theirs to write in.
/// </para>
/// </remarks>
public sealed record ReplyDraft
{
    /// <summary>The greatest length a drafted body carries before it is shortened to it.</summary>
    /// <remarks>A reply somebody edits rather than a document: past this the draft has stopped saving the work of writing one and started making the work of cutting one down.</remarks>
    public const int MaximumBodyLength = 8_000;

    /// <summary>The greatest number of claims one draft carries.</summary>
    /// <remarks>What a person checks before sending, which is a list they read rather than a second document. A producer naming more than this is annotating every sentence rather than what the reply asserts.</remarks>
    public const int MaximumClaims = 12;

    /// <summary>The greatest number of recipients one draft may propose.</summary>
    /// <remarks>A reply goes to the people in the exchange. A proposal longer than this is a producer addressing a mailing list, and the bound holds whatever the conversation's own size is.</remarks>
    public const int MaximumProposedRecipients = 10;

    /// <summary>What the plain reading writes between two lines of one block: a list's items, a table's rows.</summary>
    public const string LineBreak = "\n";

    /// <summary>What the plain reading writes between two blocks.</summary>
    public const string BlockBreak = "\n\n";

    /// <summary>What the plain reading writes between two cells of one table row.</summary>
    public const string CellBreak = " | ";

    private ReplyDraft(
        bool wasWritten,
        MailDocument document,
        string body,
        IReadOnlyList<ReplyDraftClaim> claims,
        IReadOnlyList<EmailAddress> proposedRecipients)
    {
        this.WasWritten = wasWritten;
        this.Document = document;
        this.Body = body;
        this.Claims = claims;
        this.ProposedRecipients = proposedRecipients;
    }

    /// <summary>Gets the answer a drafting that produced nothing gives, which leaves the composer as it was.</summary>
    public static ReplyDraft Nothing { get; } = new(
        wasWritten: false,
        MailDocument.Reduced(
            [],
            removedRemoteReferenceCount: 0,
            retainedRemoteImageCount: 0,
            inlineImageCount: 0,
            undrawnInlineImageCount: 0,
            truncated: false),
        string.Empty,
        [],
        []);

    /// <summary>Gets whether a draft was written at all.</summary>
    public bool WasWritten { get; }

    /// <summary>Gets the drafted reply as the composer's closed set of blocks, which holds none where none was written.</summary>
    public MailDocument Document { get; }

    /// <summary>Gets the drafted reply as plain text: this deployment's reading of <see cref="Document" />, never a second text a producer wrote.</summary>
    public string Body { get; }

    /// <summary>Gets what the draft asserts, in the order the producer wrote it, each with the messages backing it or with none.</summary>
    public IReadOnlyList<ReplyDraftClaim> Claims { get; }

    /// <summary>Gets the people the draft proposes to reach, each of whom the conversation itself names.</summary>
    public IReadOnlyList<EmailAddress> ProposedRecipients { get; }

    /// <summary>Records one drafted reply written as plain text, which is paragraphs of plain runs.</summary>
    /// <param name="body">The drafted text, a blank line between paragraphs.</param>
    /// <param name="claims">What it asserts, with what backs each.</param>
    /// <param name="proposedRecipients">The people it proposes to reach.</param>
    /// <returns>The draft, with the body shortened to its bound and the two lists to theirs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the body is blank, a drafted reply being text somebody can edit rather than an absence dressed as one.</exception>
    public static ReplyDraft Written(
        string body,
        IReadOnlyList<ReplyDraftClaim> claims,
        IReadOnlyList<EmailAddress> proposedRecipients)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return Written(
            MailDocument.Reduced(
                [
                    .. MailTextBounds.TruncateAtTextElementBoundary(body, MaximumBodyLength)
                        .ReplaceLineEndings(LineBreak)
                        .Split(BlockBreak, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(static paragraph => new MailParagraphBlock(
                            [new MailInlineRun(paragraph, MailTextEmphasis.None, Foreground: null, Link: null)],
                            MailBlockAlignment.Inherited)),
                ],
                removedRemoteReferenceCount: 0,
                retainedRemoteImageCount: 0,
                inlineImageCount: 0,
                undrawnInlineImageCount: 0,
                truncated: false),
            claims,
            proposedRecipients);
    }

    /// <summary>Records one drafted reply written as blocks.</summary>
    /// <param name="document">The drafted reply, already held to the composer's closed set and to its bounds.</param>
    /// <param name="claims">What it asserts, with what backs each.</param>
    /// <param name="proposedRecipients">The people it proposes to reach.</param>
    /// <returns>The draft, with its plain text read from the blocks and the two lists shortened to their bounds.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the blocks hold no words, a drafted reply being text somebody can edit rather than an absence dressed as one.</exception>
    public static ReplyDraft Written(
        MailDocument document,
        IReadOnlyList<ReplyDraftClaim> claims,
        IReadOnlyList<EmailAddress> proposedRecipients)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(proposedRecipients);

        var body = PlainTextOf(document.Blocks);

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("A drafted reply holds words somebody can edit.", nameof(document));
        }

        // The reading that builds a draft charges every separator above against the bound, so this cut is reached only
        // where a scanner's replacement came out longer than the words it replaced.
        return new ReplyDraft(
            wasWritten: true,
            document,
            MailTextBounds.TruncateAtTextElementBoundary(body, MaximumBodyLength),
            [.. claims.Take(MaximumClaims)],
            [.. proposedRecipients.Take(MaximumProposedRecipients)]);
    }

    /// <summary>Reads the blocks a draft is written in as plain text: a blank line between blocks, a line per list item and per table row.</summary>
    /// <remarks>Only the three blocks a draft is written in carry words here, because the reading that builds a draft writes no other.</remarks>
    private static string PlainTextOf(IReadOnlyList<MailDocumentBlock> blocks) => string.Join(
        BlockBreak,
        blocks.Select(PlainTextOf).Where(static text => !string.IsNullOrWhiteSpace(text)));

    private static string PlainTextOf(MailDocumentBlock block) => block switch
    {
        MailParagraphBlock paragraph => string.Concat(paragraph.Content.Select(static run => run.Text)),
        MailListBlock list => string.Join(
            LineBreak,
            list.Items.Select((item, index) => (list.Ordered ? (index + 1).ToString(CultureInfo.InvariantCulture) + ". " : "- ") + PlainTextOf(item.Blocks))),
        MailTableBlock table => string.Join(
            LineBreak,
            table.Rows.Select(static row => string.Join(CellBreak, row.Cells.Select(static cell => PlainTextOf(cell.Blocks))))),
        _ => string.Empty,
    };
}
