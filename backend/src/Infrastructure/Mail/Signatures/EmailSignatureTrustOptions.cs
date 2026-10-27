// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>What a received message's S/MIME or OpenPGP signer is trusted against, and how long revocation may take.</summary>
/// <remarks>
/// The authorities and keys are read per verification rather than once at start, through the same secret resolution
/// every other configured certificate goes through, so replacing a configured root or key takes effect on the next
/// signed message without a restart.
/// </remarks>
public sealed class EmailSignatureTrustOptions
{
    /// <summary>Gets or sets whether an S/MIME certificate chaining to the operating system's trust store is trusted.</summary>
    public bool TrustSystemStore { get; set; } = true;

    /// <summary>Gets or sets the certificate authorities an S/MIME certificate is trusted for chaining to, beside the system store.</summary>
    public IReadOnlyList<ConfiguredSecret> TrustedAuthorities { get; set; } = [];

    /// <summary>Gets or sets the OpenPGP public keys whose signatures are trusted; nothing else is.</summary>
    public IReadOnlyList<ConfiguredSecret> TrustedOpenPgpKeys { get; set; } = [];

    /// <summary>Gets or sets how long establishing one certificate's revocation status may take, over every responder it names.</summary>
    public TimeSpan RevocationTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
