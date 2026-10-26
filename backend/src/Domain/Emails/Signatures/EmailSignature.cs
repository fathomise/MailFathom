// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Signatures;

/// <summary>What verifying one message's S/MIME or OpenPGP signature found, step by step.</summary>
/// <remarks>
/// <para>
/// <b>It says what the cryptography said, and never that the message is safe.</b> A verified signature binds the signed
/// content to a key and that key to an identity this deployment trusts; it says nothing about whether what the content
/// asks for is wise, and it is a separate fact from what the receiving server established about the sender.
/// </para>
/// <para>
/// <b>The steps are kept beside the outcome they add up to.</b> They run in a fixed order — the content against the
/// signature, the signer against an authority or a configured key, the signer's validity and purpose at the signing
/// time, its revocation, and whether it names the displayed author — and each records its own result, so a reader is
/// shown which step decided the outcome and which were never reached. <see cref="Outcome" /> is derived from them and
/// <see cref="UnverifiableReason" /> rather than stated beside them, so the two can never disagree.
/// </para>
/// <para>
/// Every message carries one of these. <see cref="NotSigned" /> is the answer for a message with no signature, and a
/// message that carries an S/MIME part signing nothing — <c>certs-only</c> — records its form with the same answer.
/// </para>
/// </remarks>
public sealed record EmailSignature
{
    private readonly string? chainEnd;

    /// <summary>Gets the verification of a message that carries no signature.</summary>
    public static EmailSignature NotSigned { get; } = new();

    /// <summary>Gets the form the signature arrived in, or <see langword="null" /> for a message with no signed part.</summary>
    public EmailSignatureFormat? Format { get; init; }

    /// <summary>Gets the <c>smime-type</c> an opaque S/MIME part declared; present for that form alone.</summary>
    public SecureMimeContentType? SecureMimeType { get; init; }

    /// <summary>Gets why the signature is not verifiable here, or <see langword="null" /> where it is.</summary>
    /// <remarks>The first step, in the order they run, that neither passed nor failed the content.</remarks>
    public EmailSignatureUnverifiableReason? UnverifiableReason { get; init; }

    /// <summary>Gets the signing time the signature claims, or <see langword="null" /> where it claims none.</summary>
    /// <remarks>
    /// The signer wrote it, so it is a claim rather than a measurement. It is still the instant validity is judged at,
    /// as mail clients judge it, because a signature made while a certificate was valid stays a signature made then.
    /// </remarks>
    public DateTimeOffset? SignedAt { get; init; }

    /// <summary>Gets whether the signature matches the signed content.</summary>
    public SignatureCheckResult ContentCheck { get; init; } = SignatureCheckResult.Skipped;

    /// <summary>Gets whether the signer chains to a trusted certificate authority, or is a configured OpenPGP key.</summary>
    public SignatureCheckResult AuthorityCheck { get; init; } = SignatureCheckResult.Skipped;

    /// <summary>Gets where the trust came from, present exactly when <see cref="AuthorityCheck" /> passed.</summary>
    public SignatureTrustAnchor? TrustAnchor { get; init; }

    /// <summary>Gets the subject name of the certificate the S/MIME chain ended at, whether or not it is trusted.</summary>
    /// <remarks>
    /// Named whether the chain ended at a trusted root or not, so a reader shown an untrusted signature sees which
    /// authority it claimed rather than only that it was not one of ours. Bounded like a signer's name.
    /// </remarks>
    public string? ChainEnd
    {
        get => this.chainEnd;
        init => this.chainEnd = value is { Length: > EmailSigner.MaximumNameLength } ? value[..EmailSigner.MaximumNameLength] : value;
    }

    /// <summary>Gets whether the signer was valid at the signing time and meant for signing email.</summary>
    public SignatureCheckResult ValidityCheck { get; init; } = SignatureCheckResult.Skipped;

    /// <summary>Gets what <see cref="ValidityCheck" /> objected to, present exactly when it failed.</summary>
    /// <remarks>
    /// One of <see cref="EmailSignatureUnverifiableReason.OutsideValidityPeriod" />,
    /// <see cref="EmailSignatureUnverifiableReason.WrongKeyUsage" />, or
    /// <see cref="EmailSignatureUnverifiableReason.ExpiredKey" />. Kept apart from <see cref="UnverifiableReason" />,
    /// which names only the first failing step: a signature from an untrusted authority can also be expired.
    /// </remarks>
    public EmailSignatureUnverifiableReason? ValidityFailure { get; init; }

    /// <summary>Gets whether the signer is free of a revocation.</summary>
    public SignatureCheckResult RevocationCheck { get; init; } = SignatureCheckResult.Skipped;

    /// <summary>Gets how the revocation status was established, present where an answer was obtained.</summary>
    public RevocationCheckMethod? RevocationMethod { get; init; }

    /// <summary>Gets when the revocation answer was obtained; present for an online answer.</summary>
    public DateTimeOffset? RevocationCheckedAt { get; init; }

    /// <summary>Gets when the signer was revoked, where the answer said it was and said when.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Gets whether the signer's certificate or key names the address the message displays as its author.</summary>
    public SignatureCheckResult SignerCheck { get; init; } = SignatureCheckResult.Skipped;

    /// <summary>Gets who the signature names, as far as it could be read; absent where it names nobody readable.</summary>
    public EmailSigner? Signer { get; init; }

    /// <summary>Gets what the verification concluded, derived from the steps.</summary>
    /// <remarks>
    /// A content mismatch is <see cref="EmailSignatureOutcome.Failed" /> whatever else was found, because it is the one
    /// finding that says the message is not what was signed. Otherwise a named reason makes it
    /// <see cref="EmailSignatureOutcome.NotVerifiable" />, a content check that passed with no reason makes it
    /// <see cref="EmailSignatureOutcome.Verified" />, and a content check nothing reached is a message that signed
    /// nothing.
    /// </remarks>
    public EmailSignatureOutcome Outcome =>
        this.ContentCheck == SignatureCheckResult.Failed ? EmailSignatureOutcome.Failed
        : this.UnverifiableReason is not null ? EmailSignatureOutcome.NotVerifiable
        : this.ContentCheck == SignatureCheckResult.Passed ? EmailSignatureOutcome.Verified
        : EmailSignatureOutcome.NotSigned;

    /// <summary>Gets whether the signer names the displayed author, which is a separate fact from <see cref="Outcome" />.</summary>
    public bool SignerMatchesAuthor => this.SignerCheck == SignatureCheckResult.Passed;
}
