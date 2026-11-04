// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Releases the stored message of every delivered send that still holds one, a bounded number per run.</summary>
/// <remarks>
/// <para>
/// A delivered send's payload is released by <see cref="OutgoingMailFilingPass" /> the moment its sent copy settles, so
/// what reaches this is what that moment missed: the sends delivered before the release existed, and a send whose pass
/// stopped between its delivery and its filing. Nothing files a delivered send's copy after the pass that delivered it,
/// so once the age floor has passed nothing can still be reading the bytes.
/// </para>
/// <para>
/// The sweep is one run for the whole deployment, so it runs only under the lease on its scope, as
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// decides. A replica refused the lease releases nothing and asks again on its next interval; and because the release
/// is a conditional statement, a run that did overlap another would remove each payload once.
/// </para>
/// <para>
/// Each batch commits on its own, so a run stopped part-way has released exactly what it counted and the backlog of a
/// deployment that sent a great deal before this existed is worked down over several runs rather than in one
/// transaction over the whole table.
/// </para>
/// </remarks>
public sealed class DeliveredOutgoingContentSweep
{
    /// <summary>How long a send has been delivered before the sweep may release its payload.</summary>
    /// <remarks>
    /// Far longer than a pass takes to file the sent copy after the delivery it just recorded, which is the one reader a
    /// delivered send's bytes ever have, including on a replica of the previous build during a rolling upgrade.
    /// </remarks>
    public static readonly TimeSpan MinimumDeliveredAge = TimeSpan.FromHours(1);

    /// <summary>How many payloads one batch releases at most.</summary>
    public const int BatchSize = 500;

    /// <summary>How many batches one run commits at most.</summary>
    public const int MaximumBatchesPerRun = 20;

    /// <summary>The lease the sweep is held under, which is the deployment's as the sweep is.</summary>
    internal static readonly WorkScope SweepScope = WorkScope.Create("delivered-outgoing-content-sweep");

    private readonly AccessAuthorization authorization;
    private readonly IEmailContentStore contents;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly IWorkLeaseRunner leases;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the sweep.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="contents">Names the delivered sends still holding a payload and releases them.</param>
    /// <param name="commitPolicy">Commits each batch.</param>
    /// <param name="leases">Holds the sweep's scope for the length of one run.</param>
    /// <param name="timeProvider">Reads the instant the age floor is measured from.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public DeliveredOutgoingContentSweep(
        AccessAuthorization authorization,
        IEmailContentStore contents,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IWorkLeaseRunner leases,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.contents = contents;
        this.commitPolicy = commitPolicy;
        this.leases = leases;
        this.timeProvider = timeProvider;
    }

    /// <summary>Runs one bounded sweep if this replica takes its lease.</summary>
    /// <param name="cancellationToken">Cancels the claim and the run between batches.</param>
    /// <returns>How many payloads the run released, or <see langword="null" /> when another replica holds the sweep.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when anything but this deployment's own process reached the use case.</exception>
    public async Task<int?> RunAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequireProcessIdentity();

        var released = 0;

        var ran = await this.leases.TryRunUnderLeaseAsync(
            SweepScope,
            async heldToken => released = await this.SweepAsync(heldToken),
            cancellationToken);

        return ran ? released : null;
    }

    private async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var deliveredOnOrBefore = this.timeProvider.GetUtcNow() - MinimumDeliveredAge;
        var released = 0;

        for (var batch = 0; batch < MaximumBatchesPerRun; batch++)
        {
            var delivered = await this.contents.FindDeliveredOutgoingContentAsync(
                deliveredOnOrBefore,
                BatchSize,
                cancellationToken);

            if (delivered.Count == 0)
            {
                break;
            }

            released += await this.commitPolicy.CommitAsync(
                (session, token) => this.contents.ReleaseDeliveredOutgoingContentAsync(session, delivered, token),
                cancellationToken);

            if (delivered.Count < BatchSize)
            {
                break;
            }
        }

        return released;
    }
}
