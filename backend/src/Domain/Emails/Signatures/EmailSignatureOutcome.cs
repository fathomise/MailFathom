// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Signatures;

/// <summary>What verifying a message's S/MIME or OpenPGP signature concluded, the same four answers for both formats.</summary>
/// <remarks>
/// Whether the signer is the displayed author is deliberately not a fifth answer. A signature can verify and still have
/// been made by somebody other than the address the message is from, and folding that into this value would make a
/// verified signature by a stranger indistinguishable from a forged one.
/// </remarks>
public enum EmailSignatureOutcome
{
    /// <summary>The message carries no signature.</summary>
    NotSigned = 0,

    /// <summary>The signature matches the content and every trust check on the signer passed.</summary>
    Verified = 1,

    /// <summary>The signature does not match the content: the message changed after it was signed, or the signature is forged.</summary>
    Failed = 2,

    /// <summary>The signature could not be verified here, which is never a statement that it was forged.</summary>
    /// <remarks><see cref="EmailSignature.UnverifiableReason" /> names why.</remarks>
    NotVerifiable = 3,
}
