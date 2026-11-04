// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Filing;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Filing;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Synchronization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the append half of a message filed into another account, and the removal it holds back, against real PostgreSQL.</summary>
/// <remarks>
/// <para>
/// What holds the removal back is a correlated subquery the outstanding read of the source's records runs over the
/// filings table, and what the append is handed is a projection joined onto the destination's stored message. Both are
/// translated SQL a substitute would only be written to agree with, so they are asserted against the rows themselves.
/// </para>
/// <para>
/// The destination is an account of its own, bound here, and its stored message is written the way a local copy is:
/// a row holding no occurrence until the server's answer gives it one. Every test owns a UID of its own, because the
/// removal record and the hidden source are both keyed on the one message.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedCrossAccountFilingStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private const string SourceFolderAlias = "cross-account-filing-source";

    private const string DestinationFolderAlias = "cross-account-filing-destination";

    private const string DestinationAccountValue = "cross-account-filing-destination";

    private const uint AppendedUid = 6491;

    private static readonly MailAccountId DestinationAccount = MailAccountId.Create(DestinationAccountValue);

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("cross-account-filing");

    /// <summary>
    /// A mirrored source's removal is recorded with the filing and stays out of every convergence pass until the append
    /// has landed, so the message is in two mailboxes for a while and never in none; the append binds the destination's
    /// copy where the server put it and releases the removal in the same commit.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_AFilingFromAMirroredSource_HoldsTheRemovalBackUntilTheAppendLandsAndBindsTheCopy()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var filed = await ArrangeAsync(services, uid: 6401U, cancellationToken);
        var removal = await OpenRemovalAsync(services, filed, cancellationToken);
        var filing = await RecordAsync(services, filed, removal, erasesHeldSource: false, cancellationToken);

        // Act
        var eligibleWhileRecorded = await OverlayingMutationRecords.ReadEligibleAsync(services, [removal], cancellationToken);
        var outstanding = await ReadOutstandingAsync(services, filing, cancellationToken);

        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<ICrossAccountFilingStore>()
                .RecordAppendIssuedAsync(session, filing, token),
            cancellationToken);
        var outstandingOnceIssued = await ReadOutstandingAsync(services, filing, cancellationToken);
        var eligibleOnceIssued = await OverlayingMutationRecords.ReadEligibleAsync(services, [removal], cancellationToken);

        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<ICrossAccountFilingStore>().CompleteAsync(
                session,
                filing,
                RemoteEmailPlacement.Reported(ImapUidValidity.Create(SyntheticEmail.UidValidity), ImapUid.Create(AppendedUid)),
                token),
            cancellationToken);
        var outstandingOnceAppended = await ReadOutstandingAsync(services, filing, cancellationToken);
        var eligibleOnceAppended = await OverlayingMutationRecords.ReadEligibleAsync(services, [removal], cancellationToken);

        // Assert
        Assert.Empty(eligibleWhileRecorded);
        Assert.Empty(eligibleOnceIssued);
        Assert.Equal([removal], eligibleOnceAppended);

        Assert.NotNull(outstanding);
        Assert.Equal(CrossAccountFilingStage.Recorded, outstanding.Filing.Stage);
        Assert.Equal(removal, outstanding.Filing.RemovalRecordId);
        Assert.Equal(filed.Source, outstanding.Filing.SourceEmailId);
        Assert.Equal(SyntheticMailAccount.Account, outstanding.Filing.SourceAccountId);
        Assert.Equal(filed.Destination, outstanding.Filing.DestinationEmailId);
        Assert.Equal(filed.DestinationBinding.RemotePath.Value, outstanding.Filing.DestinationFolderPath.Value);
        Assert.Equal(filed.DestinationBinding.Id, outstanding.Folder?.Id);
        Assert.NotNull(outstanding.State);
        Assert.False(outstanding.State.IsSeen);
        Assert.False(outstanding.State.IsFlagged);
        Assert.Empty(outstanding.State.Keywords.Values);

        var copy = await ReadStoredEmailAsync(services, filed.Destination, cancellationToken);
        Assert.Equal(copy.ReceivedAt ?? copy.SentAt ?? copy.StoredAt, outstanding.InternalDate);

        Assert.Equal(CrossAccountFilingStage.AppendIssued, outstandingOnceIssued?.Filing.Stage);
        Assert.Null(outstandingOnceAppended);

        var bound = await ReadStoredEmailAsync(services, filed.Destination, cancellationToken);
        Assert.Equal(SyntheticEmail.UidValidity, bound.UidValidity);
        Assert.Equal(AppendedUid, bound.Uid);
    }

    /// <summary>
    /// A held source has no removal record whose overlay would hide it, so the filing hides its row in the commit that
    /// records it, and the source is out of the listings before the append is ever asked for.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AFilingFromAHeldSource_HidesTheSourceInTheSameCommit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var filed = await ArrangeAsync(services, uid: 6402U, cancellationToken);
        var sourceBefore = await ReadStoredEmailAsync(services, filed.Source, cancellationToken);

        // Act
        var filing = await RecordAsync(services, filed, removal: null, erasesHeldSource: true, cancellationToken);

        // Assert
        Assert.Null(sourceBefore.LocalDeleteCommittedAt);
        Assert.NotNull((await ReadStoredEmailAsync(services, filed.Source, cancellationToken)).LocalDeleteCommittedAt);

        var outstanding = await ReadOutstandingAsync(services, filing, cancellationToken);
        Assert.NotNull(outstanding);
        Assert.True(outstanding.Filing.ErasesHeldSource);
        Assert.Null(outstanding.Filing.RemovalRecordId);
    }

    /// <summary>
    /// Giving a filing up reverts the whole act in one commit: the destination's copy is erased, the removal it held
    /// back is withdrawn before anything issued it, the source shows again where it was, and the filing is found by its
    /// removal as abandoned rather than read as work still owed.
    /// </summary>
    [Fact]
    public async Task AbandonAsync_AFilingStillRecorded_ErasesTheCopyWithdrawsTheRemovalAndShowsTheSourceAgain()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var filed = await ArrangeAsync(services, uid: 6403U, cancellationToken);
        var removal = await OpenRemovalAsync(services, filed, cancellationToken);
        var filing = await RecordAsync(services, filed, removal, erasesHeldSource: false, cancellationToken);
        var sourceBeforeAbandoning = await ReadStoredEmailAsync(services, filed.Source, cancellationToken);

        // Act
        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<ICrossAccountFilingStore>()
                .AbandonAsync(session, filing, token),
            cancellationToken);

        // Assert
        Assert.NotNull(sourceBeforeAbandoning.LocalDeleteCommittedAt);
        Assert.False(await StoredEmailExistsAsync(services, filed.Destination, cancellationToken));
        Assert.Equal(
            MailboxMutationStage.Cancelled,
            (await OverlayingMutationRecords.ReadAsync(services, removal, cancellationToken)).Stage);
        Assert.Null((await ReadStoredEmailAsync(services, filed.Source, cancellationToken)).LocalDeleteCommittedAt);
        Assert.Null(await ReadOutstandingAsync(services, filing, cancellationToken));

        var found = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<ICrossAccountFilingStore>().FindByRemovalAsync(removal, token),
            cancellationToken);
        Assert.NotNull(found);
        Assert.Equal(filing, found.Id);
        Assert.Equal(CrossAccountFilingStage.Abandoned, found.Stage);
    }

    /// <summary>A filing names two accounts and no key reaches it, so erasing either account's mail takes the filing with it.</summary>
    [Fact]
    public async Task EraseMailOfAccountAsync_TheAccountAMessageWasFiledInto_TakesTheFilingWithIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var filed = await ArrangeAsync(services, uid: 6404U, cancellationToken);
        var filing = await RecordAsync(services, filed, removal: null, erasesHeldSource: false, cancellationToken);
        var recordedBeforeErasing = await FilingExistsAsync(services, filing, cancellationToken);

        // Act
        await services.CommitAsync(
            (scope, session, token) => UserAccountErasure.EraseMailOfAccountAsync(session, DestinationAccountValue, token),
            cancellationToken);

        // Assert
        Assert.True(recordedBeforeErasing);
        Assert.False(await FilingExistsAsync(services, filing, cancellationToken));
        Assert.True(await StoredEmailExistsAsync(services, filed.Source, cancellationToken));
    }

    /// <summary>Stores the source on the served account and the destination's copy on an account of its own, which holds no occurrence yet.</summary>
    private static async Task<FiledMessage> ArrangeAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken)
    {
        var sourceBinding = await OrchestratedFolderBinding.CommitAsync(services, SourceFolderAlias, cancellationToken);
        var destinationBinding = await OrchestratedFolderBinding.CommitAsync(
            services,
            DestinationAccount,
            DestinationFolderAlias,
            DestinationFolderAlias,
            cancellationToken);
        var sourceOccurrence = SyntheticEmail.OccurrenceIn(sourceBinding, uid);
        var subject = $"cross-account-filing-{uid}";

        var source = await StoredSyntheticEmail.MetadataOnlyAsync(services, sourceOccurrence, subject, cancellationToken);
        var destination = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            SyntheticEmail.OccurrenceIn(DestinationAccount, destinationBinding, uid),
            subject,
            cancellationToken);

        await OverlayingMutationRecords.SetStoredStateAsync(
            services,
            destination,
            row =>
            {
                row.UidValidity = null;
                row.Uid = null;
            },
            cancellationToken);

        return new FiledMessage(source, sourceOccurrence, destination, destinationBinding);
    }

    /// <summary>Opens the delete record a mirrored source's removal is, the way the filer opens it.</summary>
    private static async Task<MailboxMutationRecordId> OpenRemovalAsync(
        OrchestratedMailFathomServices services,
        FiledMessage filed,
        CancellationToken cancellationToken)
    {
        var opened = await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.Delete(
                filed.Source,
                filed.SourceOccurrence,
                Requester,
                AuthoredDeleteEmailDisposition.EraseLocalCopy,
                AuthoredDeleteServerDisposition.Expunge),
            OverlayingMutationRecords.NothingObserved,
            cancellationToken);

        return opened.Record.Id;
    }

    private static Task<CrossAccountFilingId> RecordAsync(
        OrchestratedMailFathomServices services,
        FiledMessage filed,
        MailboxMutationRecordId? removal,
        bool erasesHeldSource,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<ICrossAccountFilingStore>().RecordAsync(
                session,
                new NewCrossAccountFiling(
                    DestinationAccount,
                    filed.Destination,
                    filed.DestinationBinding.RemotePath,
                    SyntheticMailAccount.Account,
                    filed.Source,
                    removal,
                    erasesHeldSource),
                token),
            cancellationToken);

    /// <summary>Reads what the destination's converger is handed, narrowed to the one filing a test recorded.</summary>
    private static async Task<OutstandingCrossAccountFiling?> ReadOutstandingAsync(
        OrchestratedMailFathomServices services,
        CrossAccountFilingId filing,
        CancellationToken cancellationToken)
    {
        var outstanding = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<ICrossAccountFilingStore>()
                .ReadOutstandingAsync(DestinationAccount, maximumCount: 1000, token),
            cancellationToken);

        return outstanding.SingleOrDefault(candidate => candidate.Filing.Id == filing);
    }

    private static Task<StoredRow> ReadStoredEmailAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(email => email.Id == storedEmailId.Value)
                .Select(email => new StoredRow(
                    email.UidValidity,
                    email.Uid,
                    email.LocalDeleteCommittedAt,
                    email.ReceivedAt,
                    email.SentAt,
                    email.StoredAt))
                .SingleAsync(token),
            cancellationToken);

    private static Task<bool> StoredEmailExistsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AnyAsync(email => email.Id == storedEmailId.Value, token),
            cancellationToken);

    private static Task<bool> FilingExistsAsync(
        OrchestratedMailFathomServices services,
        CrossAccountFilingId filing,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .CrossAccountFilings
                .AnyAsync(row => row.Id == filing.Value, token),
            cancellationToken);

    /// <summary>The two stored messages one filing is between, and where the destination's copy is bound.</summary>
    private sealed record FiledMessage(
        StoredEmailId Source,
        EmailOccurrenceId SourceOccurrence,
        StoredEmailId Destination,
        MailFolderResolution DestinationBinding);

    /// <summary>The columns a filing is judged by, projected rather than tracked.</summary>
    private sealed record StoredRow(
        uint? UidValidity,
        uint? Uid,
        DateTimeOffset? LocalDeleteCommittedAt,
        DateTimeOffset? ReceivedAt,
        DateTimeOffset? SentAt,
        DateTimeOffset StoredAt);
}
