// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Application.Emails.Provenance;

/// <summary>Where one stored email came from, as this deployment recorded it rather than as the email says.</summary>
/// <remarks>
/// <para>
/// A message does not have to give an order to do harm: it only has to state something false as fact — that a
/// supplier's account changed, that a colleague needs a payment approved today — and an AI operation restating it hands
/// it to the person in the assistant's voice. This is what lets an operation qualify such a claim, and it is written
/// into every turn by the service, never inferred from the text, so no message can award itself a verified status.
/// </para>
/// <para>
/// Every part is read from what the deployment stored when the message arrived: the sender verdict the summaries
/// already publish, the role of the folder the message sits in, the mail the same mailbox held before it, and the
/// address the mailbox's own account record holds. A
/// signature verification added to the sender verdict later reaches every operation through
/// <see cref="Sender" /> without anything here changing.
/// </para>
/// <para>
/// It is personal data, because whether somebody has written to a mailbox before is a fact about who they correspond
/// with, and it inherits the classification of the mail it describes.
/// </para>
/// </remarks>
public sealed record EmailProvenance
{
    /// <summary>Gets what the receiving server established about the displayed author, and whether this deployment recognizes them.</summary>
    public required SenderVerification Sender { get; init; }

    /// <summary>Gets whether the email sits in the folder its account maps as junk.</summary>
    public required bool IsInJunkFolder { get; init; }

    /// <summary>Gets whether nothing the mailbox held from before the email was written by its sender or addressed to them.</summary>
    /// <remarks>
    /// Earlier mail in the junk, trash, and drafts folders is not correspondence, for the reason contact collection
    /// ignores those folders, so a sender whose only earlier mail sat there is still a first contact. Neither is mail a
    /// tool may not read — a folder no mapping admits, a message the server expunged — because the flag reaches the
    /// model and the caller and must not tell them that such mail exists. A message carrying no usable sender address is
    /// a first contact too, since nobody can be said to have written to the mailbox before.
    /// </remarks>
    public required bool IsFirstContact { get; init; }

    /// <summary>Gets whether the mailbox itself wrote the email, which makes the person the mailbox belongs to its author.</summary>
    /// <remarks>
    /// <para>
    /// Read from what the deployment stored and never from the text: the email sits in the folder its account maps as
    /// sent mail, or its <c>From</c> address is the address the account's record holds and nothing contradicts that —
    /// either it never passed a receiving server, which is a copy the person's own client filed, or the receiving server
    /// authenticated that author. A received message merely displaying the mailbox's address is neither, so a forger
    /// cannot write themselves out of the qualifications the other facts earn them.
    /// </para>
    /// <para>
    /// It names nobody — not the address, not the folder — which is what lets it travel to a provider beside the other
    /// facts: an operation reading a message the person sent reads its undertakings as theirs and its requests as
    /// somebody else's work. <see langword="false" /> where nothing places the email, which reads it as received mail,
    /// the reading every operation took before the fact existed.
    /// </para>
    /// </remarks>
    public bool IsWrittenByMailbox { get; init; }
}
