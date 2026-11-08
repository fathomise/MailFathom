// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Signing;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Transmission;

/// <summary>Derives the bytes a send leaves as from the message the outbox stored and the instant it leaves at.</summary>
/// <remarks>
/// <para>
/// The outbox stores a message once, when the send is asked for, and never rewrites it. A send may leave much later —
/// at its due instant, or after a refusal that cleared — so each attempt dates the stored message with the instant it
/// begins and signs it for the account's organization, and the copy filed in the sent folder is derived again from the
/// stored message and the instant the record kept. Recipients, the sent folder, and the stored copy's sent instant then
/// all name the moment the message left.
/// </para>
/// <para>
/// The derivation is deterministic while the organization's key stands: the date is whole seconds, the signature's time
/// is that same instant, and Ed25519 signs deterministically, so a sent copy filed later is the transmitted message.
/// The record keeps the latest attempt's instant, so where a send was partly accepted and its remaining recipients were
/// offered it again, the copy is the message the last attempt transmitted, and a recipient an earlier attempt reached
/// holds an earlier <c>Date</c> and signature.
/// </para>
/// </remarks>
/// <param name="composer">Rewrites the message's <c>Date</c>.</param>
/// <param name="signer">Signs the dated message for the account's organization.</param>
public sealed class TransmittedEmailForm(IAuthoredEmailComposer composer, IOrganizationMailSigner signer)
{
    /// <summary>Reads the instant an attempt beginning now dates its message with.</summary>
    /// <param name="now">The current instant.</param>
    /// <returns>The instant in whole seconds, which is all a <c>Date</c> header can carry, so the stored sent instant read back from the copy equals it.</returns>
    public static DateTimeOffset TransmissionInstantAt(DateTimeOffset now) =>
        new(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    /// <summary>Composes the bytes one attempt transmits.</summary>
    /// <param name="account">The account the send leaves from.</param>
    /// <param name="storedMime">The message the outbox stored.</param>
    /// <param name="transmittedAt">The instant the attempt began, from <see cref="TransmissionInstantAt" />.</param>
    /// <param name="cancellationToken">Cancels reading the organization's key.</param>
    /// <returns>The dated and, where the account's organization signs, signed message.</returns>
    public async Task<ReadOnlyMemory<byte>> ComposeAsync(
        MailAccountId account,
        ReadOnlyMemory<byte> storedMime,
        DateTimeOffset transmittedAt,
        CancellationToken cancellationToken) =>
        await signer.SignAsync(account, composer.DateAt(storedMime, transmittedAt), transmittedAt, cancellationToken);

    /// <summary>Composes the copy of a delivered send that is filed in the sent folder.</summary>
    /// <param name="send">The send, carrying the instant its latest transmission began.</param>
    /// <param name="storedMime">The message the outbox stored.</param>
    /// <param name="cancellationToken">Cancels reading the organization's key.</param>
    /// <returns>The message as that transmission left, or the stored message unchanged where the record kept no instant — a send an earlier build transmitted, whose stored bytes are what it sent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="send" /> is <see langword="null" />.</exception>
    public async Task<ReadOnlyMemory<byte>> ComposeSentCopyAsync(
        OutgoingEmailRecord send,
        ReadOnlyMemory<byte> storedMime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);

        return send.TransmittedAt is { } transmittedAt
            ? await this.ComposeAsync(send.AccountId, storedMime, transmittedAt, cancellationToken)
            : storedMime;
    }
}
