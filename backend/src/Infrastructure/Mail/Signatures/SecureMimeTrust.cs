// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>The S/MIME trust one verification runs against, with the configured authorities already loaded.</summary>
/// <param name="TrustSystemStore">Whether the operating system's trust store is trusted.</param>
/// <param name="ConfiguredAuthorities">The authorities the operator configured that loaded; the caller owns and disposes them.</param>
/// <param name="RevocationTimeout">How long one certificate's revocation check may take.</param>
internal sealed record SecureMimeTrust(
    bool TrustSystemStore,
    IReadOnlyList<X509Certificate2> ConfiguredAuthorities,
    TimeSpan RevocationTimeout);
