// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>Publishes what the background pass over organization signing keys could not do.</summary>
public interface IOrganizationSigningKeyTelemetry
{
    /// <summary>Records that an organization due a key got none because the deployment configures no key ring.</summary>
    /// <remarks>Counted once per organization per pass, so it keeps rising for as long as the ring stays absent rather than recording the fact once.</remarks>
    void MintRefusedWithoutKeyRing();
}
