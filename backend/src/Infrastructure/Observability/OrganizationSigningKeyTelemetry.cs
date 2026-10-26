// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes how often an organization due a signing key got none because the deployment configures no key ring.</summary>
/// <remarks>
/// The counter carries no organization: which ones lack a key is what the listing answers, and an identifier per
/// organization would be a dimension that grows with the deployment. A deployment whose ring is configured never moves it.
/// </remarks>
public sealed class OrganizationSigningKeyTelemetry : IOrganizationSigningKeyTelemetry
{
    private readonly Counter<long> unminted;

    /// <summary>Initializes the instrument the renewal pass reports through.</summary>
    public OrganizationSigningKeyTelemetry()
    {
        this.unminted = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.organization.signing_key.unminted",
            unit: "{organization}",
            description: "Organizations a renewal pass found due a signing key and could not give one, because no data-encryption key ring is configured.");
    }

    /// <inheritdoc />
    public void MintRefusedWithoutKeyRing() => this.unminted.Add(1);
}
