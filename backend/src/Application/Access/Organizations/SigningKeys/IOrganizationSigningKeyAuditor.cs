// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>Where an administrator's rotation or revocation of an organization's signing key is written down.</summary>
/// <remarks>
/// Narrow for the reason <see cref="Credentials.IUserCredentialAuditor" /> is: the sink behind it is the structured log
/// today and a durable audit store later, and neither changes a caller. A rotation the background pass performs is not
/// audited here, because nobody decided it; the pass reports it in its own log line.
/// </remarks>
public interface IOrganizationSigningKeyAuditor
{
    /// <summary>Records one administrative act on an organization's signing keys.</summary>
    /// <param name="change">What was done, to which key, by whom, and when.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the act is recorded.</returns>
    Task RecordAsync(OrganizationSigningKeyChange change, CancellationToken cancellationToken);
}
