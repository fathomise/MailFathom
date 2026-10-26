// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Mail.Signatures;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>What a received message's S/MIME or OpenPGP signer is trusted against: <c>MailSynchronization:SignatureVerification</c>.</summary>
/// <remarks>
/// <para>
/// Every received message is verified when it is stored, whatever is configured here; what this decides is which
/// signers a verification can end in <em>verified</em> for. With nothing configured, an S/MIME certificate chaining to
/// a public authority in the operating system's trust store verifies and every OpenPGP signature is not verifiable here,
/// because OpenPGP has no authorities and trusts only the keys an operator names.
/// </para>
/// <para>
/// The authorities and keys are ordinary named secret blocks, so they are provisioned, rotated, and erased like every
/// other configured certificate, and the material behind them is read per verification rather than once at start.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailSignatureVerificationOptions
{
    /// <summary>Gets or sets whether an S/MIME certificate chaining to the operating system's trust store is trusted.</summary>
    /// <remarks>
    /// On by default, because that store is what every mail client trusts for S/MIME. A deployment that trusts only its
    /// own authorities — a company that issues every certificate its correspondents use — turns it off and names them
    /// in <see cref="TrustedAuthorities" />.
    /// </remarks>
    public bool TrustSystemStore { get; set; } = true;

    /// <summary>Gets the certificate authorities an S/MIME certificate is trusted for chaining to, beside the system store.</summary>
    /// <remarks>Several entries so an authority rotates by overlap; each must load as a certificate without a private key.</remarks>
    public IList<ConfiguredSecret> TrustedAuthorities { get; } = [];

    /// <summary>Gets the OpenPGP public keys whose signatures are trusted; nothing else is.</summary>
    /// <remarks>Each entry is an armored or binary transferable public key, or several concatenated.</remarks>
    public IList<ConfiguredSecret> TrustedOpenPgpKeys { get; } = [];

    /// <summary>Gets or sets how long establishing one signer's revocation status may take, over every responder its certificate names.</summary>
    /// <remarks>
    /// A check that runs out of time makes the signature not verifiable here rather than verified or failed, and is
    /// tried again the next time the message is re-derived. It runs when a message is stored, never when one is read.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan RevocationTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Maps the settings onto what the verification reads.</summary>
    /// <returns>The trust a verification runs against.</returns>
    public EmailSignatureTrustOptions ToTrustOptions() => new()
    {
        TrustSystemStore = this.TrustSystemStore,
        TrustedAuthorities = [.. this.TrustedAuthorities],
        TrustedOpenPgpKeys = [.. this.TrustedOpenPgpKeys],
        RevocationTimeout = this.RevocationTimeout,
    };

    /// <summary>Finds what an operator must fix in the section that the data annotations do not reach.</summary>
    /// <returns>One result per faulty setting.</returns>
    /// <remarks>
    /// The section is nested, and annotations are validated on the root alone, so the range is restated here rather
    /// than trusted to an attribute nothing reads.
    /// </remarks>
    public IEnumerable<ValidationResult> FindConfigurationErrors()
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(this, new ValidationContext(this), results, validateAllProperties: true);

        return results.Select(result => new ValidationResult(
            $"MailSynchronization:SignatureVerification: {result.ErrorMessage}",
            [.. result.MemberNames.Select(member => $"{nameof(MailSynchronizationOptions.SignatureVerification)}.{member}")]));
    }
}
