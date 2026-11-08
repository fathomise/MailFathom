// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Signing;

/// <summary>Signs a message an organization's mail account sends with that organization's active signing key.</summary>
/// <remarks>
/// <para>
/// It is asked for the bytes of every transmission attempt and of every sent copy, once those have been dated with the
/// instant the send left at, because <c>Date</c> is one of the headers the signature covers: a message signed when it
/// was asked for would no longer verify once its date was rewritten. The stored message is therefore never signed, and
/// a signature it carries from an earlier build is replaced rather than stacked.
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md">ADR 0042</see>
/// decides the format and why it sits here.
/// </para>
/// <para>
/// It never refuses a send. A message from an account in no organization comes back as it was given, and so does one
/// whose organization holds no key to sign with, which the adapter counts: a signature is evidence for the recipient,
/// never a condition of delivery. So does a message the signature would carry past
/// <see cref="OutgoingEmailBounds.MaxMessageBytes" />, counted the same way, because the bound was measured
/// on the composed bytes and the transmitted ones may not exceed it.
/// </para>
/// </remarks>
public interface IOrganizationMailSigner
{
    /// <summary>Signs a message for the organization the sending account belongs to now.</summary>
    /// <param name="account">The account the message is sent from.</param>
    /// <param name="rawMime">The dated message, as it is about to be transmitted or filed.</param>
    /// <param name="signedAt">The instant the signature names as its time, which is the send's transmission instant, so signing the same message again yields the same signature while the key stands.</param>
    /// <param name="cancellationToken">Cancels reading and opening the key.</param>
    /// <returns>The message to send: the same bytes with one signature header added above them, or the bytes unchanged where nothing signs for the account, any earlier signature header of this signer removed either way.</returns>
    Task<ReadOnlyMemory<byte>> SignAsync(
        MailAccountId account,
        ReadOnlyMemory<byte> rawMime,
        DateTimeOffset signedAt,
        CancellationToken cancellationToken);
}
