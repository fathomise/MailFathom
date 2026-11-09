// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>One message as a derivation is shown it: what a reader would see at the top, and its passages.</summary>
/// <param name="StoredEmailId">The message being derived from.</param>
/// <param name="Subject">The subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="DatedAt">The instant the message is dated by, which is what a date in the text is read against; <see cref="DatedAtOf" /> chooses it.</param>
/// <param name="Passages">The message's own passages, in reading order and bounded by what one derivation may read.</param>
/// <param name="Provenance">Where the message came from as this deployment recorded it, which the derivation qualifies its reading by.</param>
/// <remarks>
/// <para>
/// Deliberately narrow. A derivation is shown the subject, the instant the message is dated by, and the text — never the
/// sender, the addressees, the folder's identity, or the account — because none of those is what a sentence about the
/// message is derived from, and every one of them is a participant this deployment would then be sending to a provider for no reading it buys.
/// </para>
/// <para>
/// The provenance is the one thing about the sender and the mailbox it is shown, and it names nobody: five facts the
/// deployment recorded — whether the author was authenticated, whether the deployment trusts them, whether the message
/// sits in junk, whether its sender is a first contact, and whether the mailbox itself wrote it. The first four are what
/// lets a derivation describe a request to change payment details as a claim rather than as a task; the fifth is what
/// lets it read a message the person sent from their side, so their own undertakings are the commitment and what they
/// asked of somebody else is nobody's task of theirs. It says which way the message went without saying who, or which
/// folder it sits in.
/// </para>
/// <para>
/// The dating instant is here so that a relative date the text states can be resolved into one a row can show. It is
/// the message's own metadata rather than the current time, which is what keeps a derivation reproducible: the same
/// message derived again next month resolves *by Friday* to the same day.
/// </para>
/// </remarks>
public sealed record EnrichableEmail(
    StoredEmailId StoredEmailId,
    string? Subject,
    DateTimeOffset? DatedAt,
    IReadOnlyList<EnrichablePassage> Passages,
    EmailProvenance Provenance)
{
    /// <summary>Chooses the instant a message is dated by, from the two its metadata may carry.</summary>
    /// <param name="receivedAt">When the last receiving hop recorded the message, or <see langword="null" /> where none did.</param>
    /// <param name="sentAt">When the message says it was sent, or <see langword="null" /> where it says nothing readable.</param>
    /// <param name="provenance">Where the message came from, which decides whose clock it is read by.</param>
    /// <returns>The instant, or <see langword="null" /> where the message carries neither.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provenance" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Mail somebody else wrote is dated by its arrival, because the sent instant is whatever the sender's client wrote
    /// and the arrival is a server's own clock; the sent instant stands in only where no hop recorded an arrival.
    /// </para>
    /// <para>
    /// Mail the mailbox wrote is dated by when it was sent, because that is what the person's own <em>I'll send it before
    /// Monday</em> is relative to and their own client wrote it. Whether such a copy carries an arrival at all depends on
    /// the provider — one stamps a <c>Received</c> header on the copy it files in Sent, another leaves a client to file
    /// it without one — so preferring the arrival would date the same message differently by provider. The arrival stands
    /// in only where the copy says nothing readable about when it was sent.
    /// </para>
    /// </remarks>
    public static DateTimeOffset? DatedAtOf(
        DateTimeOffset? receivedAt,
        DateTimeOffset? sentAt,
        EmailProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        return provenance.IsWrittenByMailbox ? sentAt ?? receivedAt : receivedAt ?? sentAt;
    }
}
