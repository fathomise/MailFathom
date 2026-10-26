// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes how often an organization got no signing key, and how often its mail left unsigned.</summary>
/// <remarks>
/// Neither counter carries an organization: which ones lack a key is what the listing answers, and an identifier per
/// organization would be a dimension that grows with the deployment. A deployment whose ring is configured and whose
/// organizations all hold an active key moves the first not at all, and the second only for a message that names no
/// signable author or that the signature would carry past the deployment's size bound.
/// </remarks>
public sealed class OrganizationSigningKeyTelemetry : IOrganizationSigningKeyTelemetry
{
    private readonly Counter<long> unminted;
    private readonly Counter<long> unsignedMessages;

    /// <summary>Initializes the instruments the renewal pass and the signing of outgoing mail report through.</summary>
    public OrganizationSigningKeyTelemetry()
    {
        this.unminted = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.organization.signing_key.unminted",
            unit: "{organization}",
            description: "Organizations a renewal pass found due a signing key and could not give one, because no data-encryption key ring is configured.");

        this.unsignedMessages = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.organization.mail.unsigned",
            unit: "{message}",
            description: "Sends an organization's mail account asked to queue without the organization's signature: no active signing key, a private half that did not open, no signable author domain, or a signature that would carry the message past MailDelivery:MaxMessageBytes.");
    }

    /// <inheritdoc />
    public void MintRefusedWithoutKeyRing() => this.unminted.Add(1);

    /// <inheritdoc />
    public void MessageSentUnsigned() => this.unsignedMessages.Add(1);
}
