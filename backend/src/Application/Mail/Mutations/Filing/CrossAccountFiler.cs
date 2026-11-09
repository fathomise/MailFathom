// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Filing;

/// <summary>Files a message into a folder of another account the person holds, as one local commit of two halves.</summary>
/// <remarks>
/// <para>
/// Which half each account takes is its custodian's answer, never a reading of its phase here, so every pairing
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>
/// axis H states follows from the two answers. The destination always gets a new stored message of its own, written from
/// the source's stored bytes, and a destination whose half is carried also gets the filing its own converger appends.
/// The source is hidden at once: a carried source by a delete record its converger holds back until that append has
/// landed, a held source by its row, erased in this commit where nothing has to land first and once the append does
/// otherwise.
/// </para>
/// <para>
/// Everything that can be refused is refused by <see cref="PrepareAsync" />, before anything is written, so the person
/// meets a sentence rather than a move undone later.
/// </para>
/// </remarks>
public sealed class CrossAccountFiler
{
    private readonly MailboxCustodianResolver custodians;
    private readonly ICrossAccountFilingStore filings;
    private readonly IMailFolderResolutionStore bindings;
    private readonly LocalMailCopier copier;
    private readonly ILocalEmailStateStore states;
    private readonly IMailboxMutationRecordStore records;

    /// <summary>Initializes the filer over the two custodians it asks and the stores both halves are written through.</summary>
    /// <param name="custodians">Answers which half each account takes.</param>
    /// <param name="filings">Keeps the append half a carried destination owes.</param>
    /// <param name="bindings">Answers where on the destination's server the folder was last found.</param>
    /// <param name="copier">Writes the destination's new stored message.</param>
    /// <param name="states">Reads what the source carries, and erases a held source.</param>
    /// <param name="records">Opens the delete record a carried source's removal is.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public CrossAccountFiler(
        MailboxCustodianResolver custodians,
        ICrossAccountFilingStore filings,
        IMailFolderResolutionStore bindings,
        LocalMailCopier copier,
        ILocalEmailStateStore states,
        IMailboxMutationRecordStore records)
    {
        ArgumentNullException.ThrowIfNull(custodians);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(copier);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(records);

        this.custodians = custodians;
        this.filings = filings;
        this.bindings = bindings;
        this.copier = copier;
        this.states = states;
        this.records = records;
    }

    /// <summary>Decides whether the filing may be made, and places the destination's payload before the transaction opens.</summary>
    /// <param name="source">Where the message is now.</param>
    /// <param name="sourceEmail">The message.</param>
    /// <param name="destinationAccount">The account it is filed into.</param>
    /// <param name="destinationAlias">The destination folder's alias on that account.</param>
    /// <param name="destinationFolder">The destination folder.</param>
    /// <param name="cancellationToken">Cancels the reading and the placement.</param>
    /// <returns>The plan the commit writes, or why the filing is refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source" /> is <see langword="null" />.</exception>
    public async Task<CrossAccountFilingPreparation> PrepareAsync(
        AuthoredMailboxTarget source,
        StoredEmailId sourceEmail,
        MailAccountId destinationAccount,
        MailFolderAlias destinationAlias,
        MailFolderId destinationFolder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var sourceAccount = source.Occurrence.AccountId;
        var sourceHalf = await (await this.custodians.ResolveAsync(sourceAccount, cancellationToken))
            .TakesCrossAccountFilingAsync(sourceAccount, cancellationToken);
        var destinationHalf = await (await this.custodians.ResolveAsync(destinationAccount, cancellationToken))
            .TakesCrossAccountFilingAsync(destinationAccount, cancellationToken);

        if ((RefusalOf(sourceHalf, isSource: true) ?? RefusalOf(destinationHalf, isSource: false)) is { } refused)
        {
            return CrossAccountFilingPreparation.Refused(refused);
        }

        if (await this.filings.IsFiledFromOutgoingRecordAsync(sourceEmail, cancellationToken))
        {
            return CrossAccountFilingPreparation.Refused(CrossAccountFilingRefusal.FiledFromOutgoingRecord);
        }

        var binding = await this.bindings.GetCurrentResolutionAsync(destinationAccount, destinationAlias, cancellationToken);
        var copy = binding is null ? null : await this.copier.PrepareAsync(destinationAccount, sourceEmail, cancellationToken);

        return binding is null || copy is null
            ? CrossAccountFilingPreparation.Missing
            : CrossAccountFilingPreparation.Planned(
                new CrossAccountFilingPlan(
                    source,
                    sourceEmail,
                    destinationAccount,
                    binding,
                    destinationFolder,
                    copy,
                    SourceIsCarried: sourceHalf == CrossAccountFilingHalf.Carried,
                    DestinationIsCarried: destinationHalf == CrossAccountFilingHalf.Carried));
    }

    /// <summary>Writes both halves of a planned filing, inside the caller's transaction.</summary>
    /// <param name="session">The transaction the filing commits in.</param>
    /// <param name="plan">What <see cref="PrepareAsync" /> planned.</param>
    /// <param name="requester">The invocation asking, which the source's removal record is recorded for.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The two changes to announce, or <see langword="null" /> where the source went while this ran.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan" /> or <paramref name="requester" /> is <see langword="null" />.</exception>
    public async Task<FiledAcrossAccounts?> CommitAsync(
        IPersistenceSession session,
        CrossAccountFilingPlan plan,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(requester);

        var sourceAccount = plan.Source.Occurrence.AccountId;

        if (await this.states.ReadAsync(session, sourceAccount, plan.SourceEmail, cancellationToken) is not { } state
            || await this.copier.CommitAsync(
                session,
                plan.Copy,
                plan.DestinationBinding.Id,
                plan.DestinationFolder,
                new CopiedMailFlags(state.IsSeen, state.IsFlagged, state.Keywords),
                cancellationToken) is not { } copy)
        {
            return null;
        }

        // A local-only source has no server copy for a record to remove, so it is erased here as a held one is.
        var sourceIsCarried = plan.SourceIsCarried && !state.IsLocalOnly;
        MailboxMutationRecordId? removal = sourceIsCarried
            ? await this.OpenRemovalAsync(session, plan, state, requester, cancellationToken)
            : null;

        if (plan.DestinationIsCarried)
        {
            await this.filings.RecordAsync(
                session,
                new NewCrossAccountFiling(
                    plan.DestinationAccount,
                    copy,
                    plan.DestinationBinding.RemotePath,
                    sourceAccount,
                    plan.SourceEmail,
                    removal,
                    ErasesHeldSource: !sourceIsCarried),
                cancellationToken);
        }
        else if (!sourceIsCarried)
        {
            await this.states.EraseAsync(session, sourceAccount, plan.SourceEmail, cancellationToken);
        }

        return new FiledAcrossAccounts(
            new AppliedMailboxChange(sourceAccount, Folder: null, plan.SourceEmail, Flags: null),
            new AppliedMailboxChange(plan.DestinationAccount, plan.DestinationFolder, copy, Flags: null));
    }

    /// <summary>Opens the delete record a carried source's removal is, which hides the source at once.</summary>
    /// <remarks>
    /// It expunges exactly the one message and erases the source's row once that is done, because the message has moved:
    /// what the source account keeps of mail deleted from it says nothing about mail filed into another account.
    /// </remarks>
    private async Task<MailboxMutationRecordId> OpenRemovalAsync(
        IPersistenceSession session,
        CrossAccountFilingPlan plan,
        LocalEmailState state,
        MailboxMutationRequester requester,
        CancellationToken cancellationToken)
    {
        var request = MailboxMutationRequest.Delete(
            plan.SourceEmail,
            plan.Source.Occurrence,
            requester,
            AuthoredDeleteEmailDisposition.EraseLocalCopy,
            AuthoredDeleteServerDisposition.Expunge);

        var opened = await this.records.OpenOverlayAsync(
            session,
            request,
            heldUntil: null,
            new MailboxMutationOverlay(state.IsSeen, state.IsFlagged, state.Keywords, state.Folder, LocalCopy: null),
            cancellationToken);

        return opened.Record.Id;
    }

    private static CrossAccountFilingRefusal? RefusalOf(CrossAccountFilingHalf half, bool isSource) => half switch
    {
        CrossAccountFilingHalf.RefusedWithoutUidPlus => isSource
            ? CrossAccountFilingRefusal.SourceWithoutUidPlus
            : CrossAccountFilingRefusal.DestinationWithoutUidPlus,
        CrossAccountFilingHalf.RefusedWhileRestoring => CrossAccountFilingRefusal.AccountRestoring,
        CrossAccountFilingHalf.RefusedDuringUpgrade => CrossAccountFilingRefusal.UpgradeInProgress,
        _ => null,
    };
}

/// <summary>What one filing into another account writes, as <see cref="CrossAccountFiler.PrepareAsync" /> planned it.</summary>
/// <param name="Source">Where the message is now.</param>
/// <param name="SourceEmail">The message.</param>
/// <param name="DestinationAccount">The account it is filed into.</param>
/// <param name="DestinationBinding">Where on that account's server the destination folder was last found.</param>
/// <param name="DestinationFolder">The destination folder.</param>
/// <param name="Copy">The destination's payload, placed before the transaction.</param>
/// <param name="SourceIsCarried">Whether the source's removal is a record its converger carries.</param>
/// <param name="DestinationIsCarried">Whether the destination's append is a filing its converger carries.</param>
public sealed record CrossAccountFilingPlan(
    AuthoredMailboxTarget Source,
    StoredEmailId SourceEmail,
    MailAccountId DestinationAccount,
    MailFolderResolution DestinationBinding,
    MailFolderId DestinationFolder,
    PreparedLocalCopy Copy,
    bool SourceIsCarried,
    bool DestinationIsCarried);

/// <summary>The two stored changes a filing into another account made, which are announced once it commits.</summary>
/// <param name="Source">The message taken out of the source's listings.</param>
/// <param name="Destination">The new message in the destination folder.</param>
public sealed record FiledAcrossAccounts(AppliedMailboxChange Source, AppliedMailboxChange Destination);

/// <summary>What preparing a filing into another account answered.</summary>
/// <param name="Plan">The plan to commit, or <see langword="null" /> where the filing is refused or a side of it is gone.</param>
/// <param name="Refusal">Why the filing is refused, or <see langword="null" />.</param>
public sealed record CrossAccountFilingPreparation(CrossAccountFilingPlan? Plan, CrossAccountFilingRefusal? Refusal)
{
    /// <summary>Gets the answer where the source's payload or the destination folder's binding is gone.</summary>
    public static CrossAccountFilingPreparation Missing { get; } = new(Plan: null, Refusal: null);

    /// <summary>Answers a filing that may be made.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The answer.</returns>
    public static CrossAccountFilingPreparation Planned(CrossAccountFilingPlan plan) => new(plan, Refusal: null);

    /// <summary>Answers a filing that is refused.</summary>
    /// <param name="refusal">Why.</param>
    /// <returns>The answer.</returns>
    public static CrossAccountFilingPreparation Refused(CrossAccountFilingRefusal refusal) => new(Plan: null, refusal);
}
