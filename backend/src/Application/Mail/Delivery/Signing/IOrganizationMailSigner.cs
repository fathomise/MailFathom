// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Signing;

/// <summary>Signs a message an organization's mail account sends with that organization's active signing key.</summary>
/// <remarks>
/// <para>
/// The outbox asks this once, for every send, before the message is stored: the stored bytes are what every submission
/// attempt sends and every filing into the sent folder appends, so signing anywhere later would either rewrite them or
/// send bytes nothing stored.
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md">ADR 0042</see>
/// decides the format and why it sits here.
/// </para>
/// <para>
/// It never refuses a send. A message from an account in no organization comes back as it was given, and so does one
/// whose organization holds no key to sign with, which the adapter counts: a signature is evidence for the recipient,
/// never a condition of delivery. So does a message the signature would carry past
/// <see cref="OutgoingEmailBounds.MaxMessageBytes" />, counted the same way, because the bound was measured
/// on the composed bytes and the stored ones may not exceed it.
/// </para>
/// </remarks>
public interface IOrganizationMailSigner
{
    /// <summary>Signs a message for the organization the sending account belongs to now.</summary>
    /// <param name="account">The account the message is sent from.</param>
    /// <param name="rawMime">The composed message, as it would otherwise be stored.</param>
    /// <param name="cancellationToken">Cancels reading and opening the key.</param>
    /// <returns>The message to store: the same bytes with one signature header added above them, or the bytes unchanged where nothing signs for the account.</returns>
    Task<ReadOnlyMemory<byte>> SignAsync(
        MailAccountId account,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken);
}
