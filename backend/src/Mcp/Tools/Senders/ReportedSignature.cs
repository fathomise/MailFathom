// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Emails.Signatures;

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Publishes what verifying an email's own S/MIME or OpenPGP signature found, as provenance.</summary>
/// <remarks>
/// <para>
/// The compact form a model reads: the outcome, its reason, and who signed. The step-by-step answer a reading pane draws
/// is the client's, and the signer's certificate or key is published by neither. Every value is one this deployment
/// computed or a closed name, apart from the signer's address and name, which a certificate or key wrote and a trusted
/// authority or the operator vouched for only where the outcome is <c>verified</c>.
/// </para>
/// <para>
/// It says what the cryptography said and never that the email is safe: a verified signature proves who signed and that
/// the signed content is unchanged, not that what it says is true.
/// </para>
/// </remarks>
[Description("What verifying this email's own S/MIME or OpenPGP signature found. It says who signed and whether the signed content is unchanged, never that the email is safe or that what it says is true.")]
internal sealed record ReportedSignature
{
    /// <summary>Gets what verification concluded.</summary>
    [Description("'verified' when the signature is sound, its signer is trusted by this deployment, valid, not revoked, and the signed content is unchanged; 'failed' when the content does not match the signature, so it changed after signing; 'notVerifiable' when it could not be verified here, for the reason in unverifiableReason; 'notSigned' when the email carries no signature of its own, which is also what mail stored before signatures were verified carries until it is re-read.")]
    public required SignatureVerificationState Outcome { get; init; }

    /// <summary>Gets how the email is signed.</summary>
    [Description("How the email is signed, or absent when it is not.")]
    public SignatureFormat? Format { get; init; }

    /// <summary>Gets why the signature could not be verified here.</summary>
    [Description("Why the signature could not be verified here, present only when outcome is 'notVerifiable'. 'untrustedRoot' and 'unknownKey' mean only that this deployment does not trust the signer, not that the signature is forged.")]
    public SignatureUnverifiableReason? UnverifiableReason { get; init; }

    /// <summary>Gets whether the signer is the author the message displays.</summary>
    [Description("Whether the signer's certificate or key names the address in the From header. A verified signature with this false was made by somebody other than the displayed author, which is worth telling the person.")]
    public required bool SignerMatchesAuthor { get; init; }

    /// <summary>Gets the address the signer's certificate or key names.</summary>
    [Description("The address the signer's certificate or key names, or absent when neither was reached. Vouched for by a trusted authority or a configured key only when outcome is 'verified'.")]
    public string? SignerAddress { get; init; }

    /// <summary>Gets when the signer says they signed.</summary>
    [Description("When the signer says they signed, or absent when the signature does not say. It is the signer's own claim.")]
    public DateTimeOffset? SignedAt { get; init; }

    /// <summary>Publishes one verification.</summary>
    /// <param name="signature">The stored verification to publish.</param>
    /// <returns>The wire representation of <paramref name="signature" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signature" /> is <see langword="null" />.</exception>
    public static ReportedSignature From(EmailSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        return new ReportedSignature
        {
            Outcome = PublishedOutcome(signature.Outcome),
            Format = signature.Format is { } format ? PublishedFormat(format) : null,
            UnverifiableReason = signature.UnverifiableReason is { } reason ? PublishedReason(reason) : null,
            SignerMatchesAuthor = signature.SignerMatchesAuthor,
            SignerAddress = signature.Signer?.Address,
            SignedAt = signature.SignedAt,
        };
    }

    private static SignatureVerificationState PublishedOutcome(EmailSignatureOutcome outcome) => outcome switch
    {
        EmailSignatureOutcome.NotSigned => SignatureVerificationState.NotSigned,
        EmailSignatureOutcome.Verified => SignatureVerificationState.Verified,
        EmailSignatureOutcome.Failed => SignatureVerificationState.Failed,
        EmailSignatureOutcome.NotVerifiable => SignatureVerificationState.NotVerifiable,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The stored signature outcome has no published protocol value."),
    };

    private static SignatureFormat PublishedFormat(EmailSignatureFormat format) => format switch
    {
        EmailSignatureFormat.SecureMimeDetached => SignatureFormat.SecureMimeDetached,
        EmailSignatureFormat.SecureMimeOpaque => SignatureFormat.SecureMimeOpaque,
        EmailSignatureFormat.OpenPgpMime => SignatureFormat.OpenPgpMime,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The stored signature format has no published protocol value."),
    };

    private static SignatureUnverifiableReason PublishedReason(EmailSignatureUnverifiableReason reason) => reason switch
    {
        EmailSignatureUnverifiableReason.NoCertificate => SignatureUnverifiableReason.NoCertificate,
        EmailSignatureUnverifiableReason.UntrustedRoot => SignatureUnverifiableReason.UntrustedRoot,
        EmailSignatureUnverifiableReason.OutsideValidityPeriod => SignatureUnverifiableReason.OutsideValidityPeriod,
        EmailSignatureUnverifiableReason.WrongKeyUsage => SignatureUnverifiableReason.WrongKeyUsage,
        EmailSignatureUnverifiableReason.Revoked => SignatureUnverifiableReason.Revoked,
        EmailSignatureUnverifiableReason.RevocationNotCheckable => SignatureUnverifiableReason.RevocationNotCheckable,
        EmailSignatureUnverifiableReason.UnknownKey => SignatureUnverifiableReason.UnknownKey,
        EmailSignatureUnverifiableReason.ExpiredKey => SignatureUnverifiableReason.ExpiredKey,
        EmailSignatureUnverifiableReason.RevokedKey => SignatureUnverifiableReason.RevokedKey,
        EmailSignatureUnverifiableReason.UnsupportedAlgorithm => SignatureUnverifiableReason.UnsupportedAlgorithm,
        EmailSignatureUnverifiableReason.MalformedSignature => SignatureUnverifiableReason.MalformedSignature,
        EmailSignatureUnverifiableReason.ContentNotOpened => SignatureUnverifiableReason.ContentNotOpened,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "The stored unverifiable reason has no published protocol value."),
    };
}
