// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Notifications;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Filing;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail.Mutations.Filing;

/// <summary>Carries to one account's server the messages a person filed into it from another account.</summary>
/// <remarks>
/// <para>
/// It runs as part of the destination account's convergence, under that account's lease and over its one write
/// connection, so no pass ever reaches a second account's server: the removal from the source is that account's own
/// delete record, which its own converger issues once this pass has written the append down as landed.
/// </para>
/// <para>
/// An append is issued at most once per filing, because issuing one twice is a second message in somebody's folder. A
/// server that could not be reached leaves the filing where it was, so an outage holds both halves in MailFathom; an
/// append that failed, or whose answer never came, gives the filing up and reverts the whole act. If such an append did
/// land after all, the destination's own synchronization finds the server's copy as it would any message, so the
/// message is then in both accounts — which is the outcome the ordering exists to allow, never one where it is in none.
/// </para>
/// </remarks>
public sealed class CrossAccountFilingPass
{
    private readonly ICrossAccountFilingStore filings;
    private readonly IMailboxWriteSessionFactory writeSessions;
    private readonly IEmailContentStore contents;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicyReader;
    private readonly ILocalEmailStateStore states;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailFailureNotifications failures;
    private readonly MailboxConvergenceOptions options;
    private readonly MailboxCustodianResolver custodians;

    /// <summary>Initializes the pass over the filings it carries and the connection it carries them over.</summary>
    /// <param name="filings">Reads the filings into an account and records how each one ends.</param>
    /// <param name="writeSessions">Opens the destination account's write connection.</param>
    /// <param name="contents">Reads the stored bytes the append carries.</param>
    /// <param name="transportSecurityPolicyReader">Answers how the destination's server may be reached.</param>
    /// <param name="states">Erases a held source once the append it waited for has landed.</param>
    /// <param name="commitPolicy">Commits each step, retrying an optimistic conflict.</param>
    /// <param name="failures">Tells the person about a filing that was given up.</param>
    /// <param name="options">Bounds how many filings one pass takes.</param>
    /// <param name="custodians">Answers whether the destination still carries its half to its server.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public CrossAccountFilingPass(
        ICrossAccountFilingStore filings,
        IMailboxWriteSessionFactory writeSessions,
        IEmailContentStore contents,
        IMailTransportSecurityPolicyReader transportSecurityPolicyReader,
        ILocalEmailStateStore states,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailFailureNotifications failures,
        MailboxConvergenceOptions options,
        MailboxCustodianResolver custodians)
    {
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(writeSessions);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicyReader);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(custodians);

        this.filings = filings;
        this.writeSessions = writeSessions;
        this.contents = contents;
        this.transportSecurityPolicyReader = transportSecurityPolicyReader;
        this.states = states;
        this.commitPolicy = commitPolicy;
        this.failures = failures;
        this.options = options;
        this.custodians = custodians;
    }

    /// <summary>Takes one bounded pass at the filings into an account.</summary>
    /// <param name="destination">The account the messages were filed into.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many filings the pass could not carry this time, which puts the account into backoff like any failed change.</returns>
    public async Task<int> CarryAsync(MailAccountId destination, CancellationToken cancellationToken)
    {
        var outstanding = await this.filings.ReadOutstandingAsync(
            destination,
            this.options.MaxMutationsPerPass,
            cancellationToken);

        // An account that stopped mirroring its source while a filing was outstanding — held or being restored since —
        // issues no command to it, so the filing waits rather than reaching a source that is no longer the truth.
        if (outstanding.Count == 0
            || await (await this.custodians.ResolveAsync(destination, cancellationToken))
                .TakesCrossAccountFilingAsync(destination, cancellationToken) is not CrossAccountFilingHalf.Carried)
        {
            return 0;
        }

        var transportSecurityPolicy = this.transportSecurityPolicyReader.GetPolicy(destination);
        var failedCount = 0;

        foreach (var candidate in outstanding)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await this.CarryOneAsync(candidate, transportSecurityPolicy, cancellationToken))
            {
                failedCount++;
            }
        }

        return failedCount;
    }

    /// <summary>Appends one filing, or gives it up where it can never be appended.</summary>
    /// <returns><see langword="false" /> where the filing failed this time, whether it was given up or left to the next pass.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass isolates one filing's failure so the account's other filings are still carried; the filing either stays where it was or is given up durably.")]
    private async Task<bool> CarryOneAsync(
        OutstandingCrossAccountFiling candidate,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        var filing = candidate.Filing;

        // An append whose answer never came may or may not have landed, and issuing it again risks a second copy, so the
        // filing ends here. A destination row that went before the append was issued has nothing left to carry.
        if (filing.Stage == CrossAccountFilingStage.AppendIssued
            || candidate.Folder is not { } folder
            || candidate.State is not { } state
            || await this.contents.FindStoredContentAsync(filing.DestinationEmailId, cancellationToken) is not { } stored)
        {
            await this.GiveUpAsync(filing, cancellationToken);

            return false;
        }

        IMailboxWriteSession session;

        try
        {
            session = await this.writeSessions.OpenForWritingAsync(
                filing.DestinationAccountId,
                folder,
                transportSecurityPolicy,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Nothing was issued, so the filing waits for the server exactly as any change does through an outage.
            return false;
        }

        await using (session)
        {
            await this.commitPolicy.CommitAsync(
                (persistence, token) => this.filings.RecordAppendIssuedAsync(persistence, filing.Id, token),
                cancellationToken);

            RemoteEmailPlacement placement;

            try
            {
                placement = await session.AppendRestoredAsync(stored.RawMime, state, candidate.InternalDate, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                await this.GiveUpAsync(filing, cancellationToken);

                return false;
            }

            await this.commitPolicy.CommitAsync(
                async (persistence, token) =>
                {
                    await this.filings.CompleteAsync(persistence, filing.Id, placement, token);

                    if (filing.ErasesHeldSource)
                    {
                        await this.states.EraseAsync(persistence, filing.SourceAccountId, filing.SourceEmailId, token);
                    }
                },
                cancellationToken);
        }

        return true;
    }

    /// <summary>Reverts the whole act and tells the person it was not made.</summary>
    private async Task GiveUpAsync(CrossAccountFilingRecord filing, CancellationToken cancellationToken)
    {
        await this.commitPolicy.CommitAsync(
            (persistence, token) => this.filings.AbandonAsync(persistence, filing.Id, token),
            cancellationToken);

        await this.failures.ReportAbandonedFilingAsync(filing, cancellationToken);
    }
}
