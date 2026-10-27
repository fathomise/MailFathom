// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Signatures;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Verifies the S/MIME or OpenPGP signature a received message carries.</summary>
/// <remarks>
/// A seam inside the parsing adapter rather than a decorator above it, for the reason the local DKIM verification is
/// one: verification needs the parsed message, and nothing above the port has it. It answers for everything a mailbox
/// can contain — a malformed block, an unsupported algorithm, an unreachable responder — with a verification rather
/// than an exception, so a signed message never stops the batch it arrived in.
/// </remarks>
internal interface IEmailSignatureVerifier
{
    /// <summary>Verifies what the message's own signature proves.</summary>
    /// <param name="message">The parsed message.</param>
    /// <param name="displayedSenderAddress">The address the message's <c>From</c> header wrote, where it wrote one.</param>
    /// <param name="cancellationToken">Cancels the verification and the revocation requests it makes.</param>
    /// <returns>The verification, which is <see cref="EmailSignature.NotSigned" /> for a message signing nothing.</returns>
    Task<EmailSignature> VerifyAsync(
        MimeMessage message,
        string? displayedSenderAddress,
        CancellationToken cancellationToken);
}
