// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Filing;

namespace MailFathom.Application.Mail.Mutations.Filing;

/// <summary>Keeps the append half of every message a person filed into another account, and what a stored message is that such a filing needs to know.</summary>
/// <remarks>
/// Every write takes the caller's session, because each one is half of something else that has to commit with it: the
/// filing beside the new stored message and the removal record, the bound occurrence beside the stage, and a revert
/// beside the abandonment that caused it.
/// </remarks>
public interface ICrossAccountFilingStore
{
    /// <summary>Writes down the append a filing owes its destination, in the transaction that filed the message.</summary>
    /// <param name="session">The transaction the filing commits in.</param>
    /// <param name="filing">What is filed where, and what of the source waits for it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The identity of the filing written.</returns>
    /// <remarks>
    /// A held source is hidden here, in the same transaction, because nothing else hides it: it has no delete record
    /// whose overlay would, and it is erased only once the append has landed.
    /// </remarks>
    Task<CrossAccountFilingId> RecordAsync(
        IPersistenceSession session,
        NewCrossAccountFiling filing,
        CancellationToken cancellationToken);

    /// <summary>Reads the oldest filings into one account that have not ended.</summary>
    /// <param name="destination">The account the messages are filed into.</param>
    /// <param name="maximumCount">How many to read at most.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The filings, oldest first, each with what its append needs.</returns>
    Task<IReadOnlyList<OutstandingCrossAccountFiling>> ReadOutstandingAsync(
        MailAccountId destination,
        int maximumCount,
        CancellationToken cancellationToken);

    /// <summary>Writes down that the append is about to be issued, before it is.</summary>
    /// <param name="session">The transaction the stage commits in.</param>
    /// <param name="filing">The filing.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the stage is staged in the session.</returns>
    /// <remarks>
    /// An <c>APPEND</c> issued twice is a second message in somebody's folder, so a filing found at this stage by a later
    /// pass is never appended again.
    /// </remarks>
    Task RecordAppendIssuedAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        CancellationToken cancellationToken);

    /// <summary>Ends a filing whose append landed, binding the destination's stored message where the server put it.</summary>
    /// <param name="session">The transaction the ending commits in.</param>
    /// <param name="filing">The filing.</param>
    /// <param name="placement">Where the server's <c>APPENDUID</c> put the message, which may name nothing.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the ending is staged in the session.</returns>
    /// <remarks>
    /// The removal record the filing held back is eligible from this commit on. A server copy another row already
    /// occupies — the folder's own pass got there first — keeps that row, and the filing's own row is erased as the
    /// duplicate it then is. A server that took the message and named nowhere for it leaves nothing to bind, so the
    /// filing's own row is erased as well and the message the destination's forward pass discovers there stands for it.
    /// </remarks>
    Task CompleteAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        RemoteEmailPlacement placement,
        CancellationToken cancellationToken);

    /// <summary>Gives a filing up and reverts the whole act with it.</summary>
    /// <param name="session">The transaction the revert commits in.</param>
    /// <param name="filing">The filing.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the revert is staged in the session.</returns>
    /// <remarks>
    /// The destination's stored message is erased, the removal record is withdrawn before it was ever issued, and the
    /// source shows again where it was. The removal is never past its recorded stage here, because nothing issues it
    /// until the filing is appended.
    /// </remarks>
    Task AbandonAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        CancellationToken cancellationToken);

    /// <summary>Finds the filing a removal record is the source half of.</summary>
    /// <param name="removal">The delete record.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The filing, or <see langword="null" /> where the record is an ordinary delete.</returns>
    Task<CrossAccountFilingRecord?> FindByRemovalAsync(
        MailboxMutationRecordId removal,
        CancellationToken cancellationToken);

    /// <summary>Answers whether a stored message is a copy one of its account's outgoing records filed.</summary>
    /// <param name="email">The message.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> where the message is a sent copy MailFathom filed.</returns>
    /// <remarks>Such a copy's join to its outgoing record holds only on its own account, so it is never filed into another.</remarks>
    Task<bool> IsFiledFromOutgoingRecordAsync(StoredEmailId email, CancellationToken cancellationToken);
}

/// <summary>What one filing into another account writes down.</summary>
/// <param name="DestinationAccountId">The account the message is filed into.</param>
/// <param name="DestinationEmailId">The new stored message written on it.</param>
/// <param name="DestinationFolderPath">Where on its server the message is appended.</param>
/// <param name="SourceAccountId">The account the message is filed out of.</param>
/// <param name="SourceEmailId">The message filed.</param>
/// <param name="RemovalRecordId">The delete record a mirrored source holds back until the append lands, or <see langword="null" /> on a held source.</param>
/// <param name="ErasesHeldSource">Whether the source is a held account whose row is hidden now and erased once the append lands.</param>
public sealed record NewCrossAccountFiling(
    MailAccountId DestinationAccountId,
    StoredEmailId DestinationEmailId,
    RemoteFolderPath DestinationFolderPath,
    MailAccountId SourceAccountId,
    StoredEmailId SourceEmailId,
    MailboxMutationRecordId? RemovalRecordId,
    bool ErasesHeldSource);

/// <summary>A filing that has not ended, with what its append needs.</summary>
/// <param name="Filing">The filing.</param>
/// <param name="Folder">The destination folder to append into, or <see langword="null" /> where the destination's stored message is gone.</param>
/// <param name="State">What the destination's stored message carries, which the append writes onto the server's copy.</param>
/// <param name="InternalDate">When the message arrived, which the append keeps.</param>
public sealed record OutstandingCrossAccountFiling(
    CrossAccountFilingRecord Filing,
    MailFolderResolution? Folder,
    RestoredEmailState? State,
    DateTimeOffset InternalDate);
