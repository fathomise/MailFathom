// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>Text to be sent, where the answer to the question is a message rather than a fact about one.</summary>
/// <remarks>
/// <para>
/// The block for "reply to them saying we accept". It is a proposal a person reads and sends, or does not: the plan
/// carries a draft and never an act, and <see cref="DraftDisposition" /> holds only states that are local.
/// </para>
/// <para>
/// The sources are what the draft was written from — the thread it answers, the terms it repeats — so the person about
/// to send it can check that the assistant read the right correspondence before they put their name to it.
/// </para>
/// </remarks>
public sealed record DraftBlock : PresentationBlock
{
    /// <summary>The greatest number of recipients one draft may name.</summary>
    /// <remarks>
    /// Well short of what a mail server would accept. A draft a run composed unprompted going to fifty people is a
    /// retrieval that widened rather than a message somebody meant to write.
    /// </remarks>
    public const int MaxRecipients = 20;

    /// <summary>Initializes text to be sent.</summary>
    /// <param name="evidence">What the correspondence does for the draft.</param>
    /// <param name="recipients">Who it is addressed to.</param>
    /// <param name="subject">Its subject.</param>
    /// <param name="body">Its body, as plain text.</param>
    /// <param name="disposition">What has become of it locally.</param>
    /// <param name="dueAt">When it would leave, where somebody asked for a later send, or <see langword="null" /> for as soon as it is sent.</param>
    /// <param name="account">MailFathom's own generated identifier for the account it is sent from, or <see langword="null" /> where the block does not say.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> or <paramref name="recipients" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no recipient is named, there are more than <see cref="MaxRecipients" /> of them, a recipient is named twice, or a text, the account included, is the unspecified default.</exception>
    public DraftBlock(
        PresentationEvidence evidence,
        IReadOnlyList<EmailAddress> recipients,
        PresentationText subject,
        PresentationText body,
        DraftDisposition disposition,
        DateTimeOffset? dueAt = null,
        PresentationText? account = null)
        : base(PresentationBlockType.Draft, evidence)
    {
        var addressed = PresentationRequirement.RequiredItems(recipients, MaxRecipients, nameof(recipients));

        if (addressed.Distinct().Count() != addressed.Count)
        {
            throw new ArgumentException("A draft names each recipient once.", nameof(recipients));
        }

        PresentationRequirement.Specified(subject, nameof(subject));
        PresentationRequirement.Specified(body, nameof(body));

        if (account is { } sender)
        {
            PresentationRequirement.Specified(sender, nameof(account));
        }

        this.Recipients = addressed;
        this.Subject = subject;
        this.Body = body;
        this.Disposition = disposition;
        this.DueAt = dueAt;
        this.Account = account;
    }

    /// <summary>Gets who the draft is addressed to.</summary>
    public IReadOnlyList<EmailAddress> Recipients { get; }

    /// <summary>Gets the draft's subject.</summary>
    public PresentationText Subject { get; }

    /// <summary>Gets the draft's body, as plain text.</summary>
    public PresentationText Body { get; }

    /// <summary>Gets what has become of the draft locally.</summary>
    public DraftDisposition Disposition { get; }

    /// <summary>Gets when the draft would leave once sent, or <see langword="null" /> where it leaves as soon as it is.</summary>
    /// <remarks>An instant rather than a time in a zone, as every other instant a plan carries is: the reader's own zone is what a client words it in.</remarks>
    public DateTimeOffset? DueAt { get; }

    /// <summary>Gets the account the draft is sent from, or <see langword="null" /> where the block does not say.</summary>
    /// <remarks>
    /// MailFathom's own generated identifier, as <see cref="AccountCoverage" /> names an account, which is what a client
    /// already holds the account's name and its footer by. It is absent only on a block stored before the account was
    /// carried, which a client draws without the account's footer rather than with a guessed one.
    /// </remarks>
    public PresentationText? Account { get; }
}
