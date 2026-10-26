// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations.SigningKeys;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Writes an administrator's rotation or revocation of an organization's signing key to the structured log as the deployment's audit record.</summary>
/// <remarks>
/// One warning per act, because both are what an operator looks for after a suspected leak: when the key stopped being
/// trusted, who said so, and which key the organization signs with now. Every line carries identifiers only — no key
/// material, which never reaches this type, and no organization name, which a listing resolves.
/// </remarks>
internal sealed partial class LoggedOrganizationSigningKeyAuditor(ILogger<LoggedOrganizationSigningKeyAuditor> logger)
    : IOrganizationSigningKeyAuditor
{
    /// <inheritdoc />
    public Task RecordAsync(OrganizationSigningKeyChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        this.LogSigningKeyChanged(
            change.Act,
            change.KeyId,
            change.OrganizationId,
            change.MintedKeyId,
            change.ActingAdministrator,
            change.OccurredAt);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Signing key {SigningKeyId} of organization {OrganizationId} was {SigningKeyAct} by "
            + "{ActingAdministrator} at {OccurredAt}; the key minted in its place is {MintedSigningKeyId}.")]
    private partial void LogSigningKeyChanged(
        OrganizationSigningKeyAct signingKeyAct,
        Guid? signingKeyId,
        Guid organizationId,
        Guid? mintedSigningKeyId,
        string actingAdministrator,
        DateTimeOffset occurredAt);
}
