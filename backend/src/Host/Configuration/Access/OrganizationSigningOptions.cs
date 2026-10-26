// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Organizations.SigningKeys;

namespace MailFathom.Host.Configuration.Access;

/// <summary>Configures how the deployment keeps the keys its organizations sign their accounts' mail with.</summary>
/// <remarks>
/// Deployment configuration rather than a settings-policy value, as
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md">ADR 0042</see>
/// decides: how long a private half may exist to be stolen is the operator's security posture rather than a preference
/// anybody in an organization holds, so every organization in a deployment rotates on the same interval.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class OrganizationSigningOptions
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "OrganizationSigning";

    /// <summary>Gets or sets how old an organization's active signing key grows before it is rotated.</summary>
    /// <remarks>
    /// Rotation bounds how long a private half exists to be stolen; it does not end a leaked key's validity, which only a
    /// revocation does. It is held between a day and a hundred years: an operator who wants no scheduled rotation
    /// writes the ceiling and revokes by hand, and anything longer could not be subtracted from the current time.
    /// </remarks>
    [Range(typeof(TimeSpan), "1.00:00:00", "36500.00:00:00")]
    public TimeSpan KeyRotationInterval { get; set; } = OrganizationSigningSettings.DefaultKeyRotationInterval;

    /// <summary>Reads the settings the renewal pass runs under.</summary>
    /// <returns>The settings.</returns>
    internal OrganizationSigningSettings ToSigningSettings() => new(this.KeyRotationInterval);
}
