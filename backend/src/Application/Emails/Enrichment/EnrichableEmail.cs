// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>One message as a derivation is shown it: what a reader would see at the top, and its passages.</summary>
/// <param name="StoredEmailId">The message being derived from.</param>
/// <param name="Subject">The subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="DatedAt">The instant the message is dated by, which is what a date in the text is read against: when the last receiving hop recorded it, or when it was sent where no hop recorded one.</param>
/// <param name="Passages">The message's own passages, in reading order and bounded by what one derivation may read.</param>
/// <param name="Provenance">Where the message came from as this deployment recorded it, which the derivation qualifies its reading by.</param>
/// <remarks>
/// <para>
/// Deliberately narrow. A derivation is shown the subject, the instant the message is dated by, and the text — never the
/// sender, the addressees, the folder's identity, or the account — because none of those is what a sentence about the message is derived from,
/// and every one of them is a participant this deployment would then be sending to a provider for no reading it buys.
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
/// <para>
/// It is the arrival instant where a receiving hop recorded one and the sent instant otherwise. The fallback is for mail
/// the person sent: the copy filed in Sent never passed a receiving server, so it carries no arrival instant, and
/// without one their own *I'll send it before Monday* would resolve to nothing. That is the same order the provenance
/// reader places an earlier Sent copy by. A message carrying neither instant is still undated.
/// </para>
/// </remarks>
public sealed record EnrichableEmail(
    StoredEmailId StoredEmailId,
    string? Subject,
    DateTimeOffset? DatedAt,
    IReadOnlyList<EnrichablePassage> Passages,
    EmailProvenance Provenance);
