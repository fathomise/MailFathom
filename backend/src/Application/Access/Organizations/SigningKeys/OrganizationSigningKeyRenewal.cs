// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Coordination;

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>The background pass that gives every organization an active signing key and rotates the ones that have grown old.</summary>
/// <remarks>
/// <para>
/// One pass covers both, because both are the same act: an organization whose active key is missing, or was minted at
/// or before <see cref="OrganizationSigningSettings.KeyRotationInterval" /> ago, is given a fresh one, and the old one —
/// if there is one — is retired and its private half deleted in the same transaction. That reaches the organizations
/// that existed before signing did, any created while the deployment had no key ring, and every organization whose key
/// is due, whether or not it has sent anything since.
/// </para>
/// <para>
/// Every replica runs the pass, and each organization is renewed under a leased scope of its own, so two replicas never
/// renew one organization at once and a slow organization does not hold the others back. The database's one-active-key
/// constraint stands behind the lease: an administrator rotating by hand while the pass runs leaves one active key, and
/// whichever of the two lost reads <see cref="OrganizationSigningKeyReplacementOutcome.AlreadyCurrent" />.
/// </para>
/// </remarks>
public sealed class OrganizationSigningKeyRenewal
{
    /// <summary>The most organizations one pass renews; the rest are found again by the next pass.</summary>
    public const int MaximumOrganizationsPerPass = 100;

    private readonly AccessAuthorization authorization;
    private readonly IOrganizationSigningKeyStore keys;
    private readonly IOrganizationSigningKeyTelemetry telemetry;
    private readonly IWorkLeaseRunner leases;
    private readonly OrganizationSigningSettings settings;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass.</summary>
    /// <param name="authorization">What the pass is admitted by, which is the process identity alone.</param>
    /// <param name="keys">Where the keys are kept.</param>
    /// <param name="telemetry">Where an organization left without a key is counted.</param>
    /// <param name="leases">What keeps one organization's renewal to one replica.</param>
    /// <param name="settings">How old an active key may grow.</param>
    /// <param name="timeProvider">The clock a key's age is measured by.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OrganizationSigningKeyRenewal(
        AccessAuthorization authorization,
        IOrganizationSigningKeyStore keys,
        IOrganizationSigningKeyTelemetry telemetry,
        IWorkLeaseRunner leases,
        OrganizationSigningSettings settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.keys = keys;
        this.telemetry = telemetry;
        this.leases = leases;
        this.settings = settings;
        this.timeProvider = timeProvider;
    }

    /// <summary>Names the leased scope one organization's renewal runs under.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <returns>The scope.</returns>
    public static WorkScope ScopeFor(Guid organizationId) => WorkScope.Create($"organization-signing-key/{organizationId:D}");

    /// <summary>Renews every organization due a key, up to <see cref="MaximumOrganizationsPerPass" />.</summary>
    /// <param name="cancellationToken">Cancels the pass; a renewal already committed stays committed.</param>
    /// <returns>How many organizations got a fresh key, and how many were due one but could not get it for want of a key ring.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the pass runs as anything but the process identity.</exception>
    public async Task<OrganizationSigningKeyRenewalPass> RunAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequireProcessIdentity();

        var asOf = this.timeProvider.GetUtcNow();
        var replaceKeyCreatedAtOrBefore = asOf - this.settings.KeyRotationInterval;

        var due = await this.keys.FindOrganizationsDueAsync(
            replaceKeyCreatedAtOrBefore,
            MaximumOrganizationsPerPass,
            cancellationToken);

        var renewed = 0;
        var unminted = 0;

        foreach (var organizationId in due)
        {
            var outcome = await this.RenewUnderLeaseAsync(organizationId, replaceKeyCreatedAtOrBefore, asOf, cancellationToken);

            if (outcome == OrganizationSigningKeyReplacementOutcome.Replaced)
            {
                renewed++;
            }
            else if (outcome == OrganizationSigningKeyReplacementOutcome.NoKeyRing)
            {
                unminted++;
                this.telemetry.MintRefusedWithoutKeyRing();
            }
        }

        return new OrganizationSigningKeyRenewalPass(renewed, unminted);
    }

    /// <summary>Renews one organization under its own lease.</summary>
    /// <returns>What the renewal did, or <see langword="null" /> when another replica holds the organization's lease and the renewal is its to make.</returns>
    private async Task<OrganizationSigningKeyReplacementOutcome?> RenewUnderLeaseAsync(
        Guid organizationId,
        DateTimeOffset replaceKeyCreatedAtOrBefore,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        OrganizationSigningKeyReplacementOutcome? outcome = null;

        await this.leases.TryRunUnderLeaseAsync(
            ScopeFor(organizationId),
            async heldToken => outcome = (await this.keys.ReplaceActiveKeyAsync(
                organizationId,
                replaceKeyCreatedAtOrBefore,
                asOf,
                heldToken)).Outcome,
            cancellationToken);

        return outcome;
    }
}
