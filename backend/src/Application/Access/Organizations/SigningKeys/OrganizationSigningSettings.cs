// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>How the deployment keeps its organizations' signing keys.</summary>
/// <param name="KeyRotationInterval">How old an active key grows before the background pass replaces it.</param>
public sealed record OrganizationSigningSettings(TimeSpan KeyRotationInterval)
{
    /// <summary>The interval a deployment that sets none rotates on.</summary>
    public static readonly TimeSpan DefaultKeyRotationInterval = TimeSpan.FromDays(365);

    /// <summary>The shortest interval a deployment may set.</summary>
    public static readonly TimeSpan MinimumKeyRotationInterval = TimeSpan.FromDays(1);

    /// <summary>The longest interval a deployment may set.</summary>
    /// <remarks>
    /// A hundred years: long enough to mean "never on a schedule", and short enough that the pass can subtract it from
    /// the current time, which a value near <see cref="TimeSpan.MaxValue" /> would make throw on every pass.
    /// </remarks>
    public static readonly TimeSpan MaximumKeyRotationInterval = TimeSpan.FromDays(36500);
}
