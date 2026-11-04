// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Domain.Delivery.Filing;

/// <summary>Reports one copy of an outgoing message MailFathom put into a folder, and what has become of it since.</summary>
/// <remarks>
/// <para>
/// It hangs off the outgoing record rather than standing on its own, because the outgoing record is what an operator,
/// <c>mfctl</c>, and the MCP surface already read as the truth about a message MailFathom is sending. A filing is one
/// more thing that record says about it — where a copy of it is — and reading provenance through the same record is
/// what keeps this from being a second mechanism beside the one mutations already use.
/// </para>
/// <para>
/// The copy comes back through synchronization as ordinary new mail, and this is the whole of what tells it apart from
/// somebody else's message. Where the server advertises <c>UIDPLUS</c> the <c>APPENDUID</c> response names the
/// occurrence exactly and <see cref="AccountsForPlacementAt" /> is the join; where it does not,
/// <see cref="AccountsForMessageAt" /> falls back to the identity the message carries in its own headers, which is the
/// nearest thing to a fact available once the server has declined to say where it put the copy.
/// </para>
/// <para>
/// Nothing here is mail content. A folder, an alias, a UID, and a message identity MailFathom minted itself are its own
/// or the server's names for things, which is what lets the row be written and read without the message.
/// </para>
/// </remarks>
public sealed record OutgoingMailFilingRecord
{
    /// <summary>How long after an append a second occurrence of the message is still read as the provider having filed its own.</summary>
    /// <remarks>
    /// It bounds a race between one append and the synchronization run that meets what the provider filed, which is
    /// minutes on any account being synchronized at all. Past it the copy has been in the person's folder long enough to
    /// have been read, replied from, moved, or flagged by another client, so both copies stand. It is a constant rather
    /// than a setting because it measures how long this system waits to recognize its own copy, which is not a quantity
    /// an operator has grounds to tune.
    /// </remarks>
    public static readonly TimeSpan ProviderCopyWindow = TimeSpan.FromMinutes(15);

    /// <summary>Gets the outgoing record this copy was filed from.</summary>
    public required OutgoingEmailId OutgoingEmailId { get; init; }

    /// <summary>Gets which place in the mailbox this copy was filed into.</summary>
    public required OutgoingMailFiling Filing { get; init; }

    /// <summary>Gets MailFathom's own name for the folder the copy went into, which is what a failure and a log line name.</summary>
    public required MailFolderAlias FolderAlias { get; init; }

    /// <summary>Gets the remote path the copy was appended to, which is what the join compares a discovery against.</summary>
    public required RemoteFolderPath FolderPath { get; init; }

    /// <summary>Gets how far the append has durably got.</summary>
    public required OutgoingMailFilingStage Stage { get; init; }

    /// <summary>Gets where the folder put the copy, as far as the server said.</summary>
    /// <remarks>
    /// It is <see cref="RemoteEmailPlacement.NotReported" /> both before the append is confirmed and after a server that
    /// advertises no <c>UIDPLUS</c> confirmed it. <see cref="Stage" /> is what tells the two apart.
    /// </remarks>
    public required RemoteEmailPlacement Placement { get; init; }

    /// <summary>Gets the <c>Message-ID</c> the appended copy carries, or <see langword="null" /> while none was read.</summary>
    /// <remarks>
    /// It is the identity MailFathom minted for the message itself, read back off the bytes that were appended rather
    /// than assumed, so the value recorded is the one a mail server will report. It is kept even where a placement was
    /// reported, because a folder recreated between the append and the discovery invalidates the UID and leaves this as
    /// the only thing still naming the copy.
    /// </remarks>
    public required string? InternetMessageId { get; init; }

    /// <summary>Gets when the append was issued.</summary>
    public required DateTimeOffset AppendedAt { get; init; }

    /// <summary>Gets when synchronization recognized the copy, or <see langword="null" /> while it has not.</summary>
    public required DateTimeOffset? ObservedAt { get; init; }

    /// <summary>Gets when the copy was taken back out of the folder, or <see langword="null" /> while it stands.</summary>
    public required DateTimeOffset? WithdrawnAt { get; init; }

    /// <summary>Gets where the provider filed a copy of its own beside this one, where synchronization met one it may remove.</summary>
    /// <remarks>
    /// It is <see cref="RemoteEmailPlacement.NotReported" /> for every filing whose provider files nothing, and for one
    /// whose provider's copy was met where removing it is not safe — that copy is stored as a message of its own instead.
    /// </remarks>
    public RemoteEmailPlacement ProviderCopy { get; init; } = RemoteEmailPlacement.NotReported();

    /// <summary>Gets when the provider's copy was removed or given up, or <see langword="null" /> while its removal is owed.</summary>
    public DateTimeOffset? ProviderCopySettledAt { get; init; }

    /// <summary>Gets whether the provider's copy of this send is still to be removed from the folder.</summary>
    public bool AwaitsProviderCopyRemoval =>
        this.ProviderCopy is { UidValidity: not null, Uid: not null } && this.ProviderCopySettledAt is null;

    /// <summary>Reports whether a newly discovered occurrence is a copy the provider filed of this send, which may be removed.</summary>
    /// <param name="discoveredFolderPath">The remote path of the folder the occurrence was discovered in.</param>
    /// <param name="discoveredUidValidity">The UIDVALIDITY that folder reports now.</param>
    /// <param name="discoveredUid">The UID the discovered occurrence carries.</param>
    /// <param name="discoveredMessageId">The <c>Message-ID</c> the server reported for the discovery.</param>
    /// <param name="discoveredAt">When synchronization met it.</param>
    /// <returns><see langword="true" /> when every condition that makes removing a message MailFathom did not compose safe holds.</returns>
    /// <remarks>
    /// Three of the conditions are this row's: the append was answered, the server named the occurrence it put the copy
    /// at, and the discovery is another occurrence of the same minted identity in the same UID space, met within
    /// <see cref="ProviderCopyWindow" /> of the append — before another client is likely to have acted on it. What the
    /// server advertises is the caller's to add, because it is read from the account rather than from the filing.
    /// </remarks>
    public bool AdmitsProviderCopyRemovalAt(
        RemoteFolderPath discoveredFolderPath,
        ImapUidValidity discoveredUidValidity,
        ImapUid discoveredUid,
        string? discoveredMessageId,
        DateTimeOffset discoveredAt) =>
        this.Filing == OutgoingMailFiling.Sent
        && this.AccountsForMessageAt(discoveredFolderPath, discoveredMessageId)
        && this.Placement is { UidValidity: { } placedUidValidity, Uid: { } placedUid }
        && placedUidValidity == discoveredUidValidity
        && placedUid != discoveredUid
        && this.ProviderCopy.Uid is null
        && discoveredAt - this.AppendedAt <= ProviderCopyWindow;

    /// <summary>Gets whether the append went out and the server's answer to it never came back.</summary>
    /// <remarks>
    /// A row here is never appended again. A second <c>APPEND</c> is a second message in the user's folder rather than a
    /// repeat of the first, and nothing the folder shows afterwards distinguishes them, so the row stands as the visible
    /// statement that the copy may or may not be there.
    /// </remarks>
    public bool HasUnknownOutcome => this.Stage == OutgoingMailFilingStage.Issued;

    /// <summary>Gets whether this copy is still in the folder as far as MailFathom knows.</summary>
    public bool IsStanding => this.Stage != OutgoingMailFilingStage.Withdrawn;

    /// <summary>Reports whether a newly discovered occurrence is the copy this filing appended.</summary>
    /// <param name="discoveredFolderPath">The remote path of the folder the occurrence was discovered in.</param>
    /// <param name="discoveredUidValidity">The UIDVALIDITY that folder reports now.</param>
    /// <param name="discoveredUid">The UID the discovered occurrence carries.</param>
    /// <returns><see langword="true" /> when the server itself named this occurrence as where it put the copy.</returns>
    /// <remarks>
    /// The UIDVALIDITY is compared as well as the UID, so a folder recreated between the append and the discovery matches
    /// nothing here: the recorded UID names a message in a UID space the folder no longer has. Such a discovery falls to
    /// <see cref="AccountsForMessageAt" />, which compares an identity a renumbering does not touch.
    /// </remarks>
    public bool AccountsForPlacementAt(
        RemoteFolderPath discoveredFolderPath,
        ImapUidValidity discoveredUidValidity,
        ImapUid discoveredUid) =>
        this.IsJoinable
        && this.NamesFolder(discoveredFolderPath)
        && this.Placement is { UidValidity: { } placedUidValidity, Uid: { } placedUid }
        && placedUidValidity == discoveredUidValidity
        && placedUid == discoveredUid;

    /// <summary>Reports whether a newly discovered occurrence carries the identity of the message this filing appended.</summary>
    /// <param name="discoveredFolderPath">The remote path of the folder the occurrence was discovered in.</param>
    /// <param name="discoveredMessageId">The <c>Message-ID</c> the server reported for the discovery, which may be absent.</param>
    /// <returns><see langword="true" /> when the discovery is this copy by the identity the message carries.</returns>
    /// <remarks>
    /// <para>
    /// This is the join for a server that advertises no <c>UIDPLUS</c>, and it is a comparison of identities rather than
    /// a search for something that looks like the message: MailFathom minted the value, it is unguessable, and it is in
    /// the bytes that were appended. What it is not is the server's own statement, which is why the reported placement is
    /// preferred wherever there is one.
    /// </para>
    /// <para>
    /// The folder is compared too, so the same message filed into two folders is joined to the filing whose folder it
    /// was found in rather than to whichever row sorted first.
    /// </para>
    /// </remarks>
    public bool AccountsForMessageAt(RemoteFolderPath discoveredFolderPath, string? discoveredMessageId) =>
        this.IsJoinable
        && this.NamesFolder(discoveredFolderPath)
        && this.InternetMessageId is { } appendedMessageId
        && discoveredMessageId is not null
        && string.Equals(appendedMessageId, discoveredMessageId, StringComparison.Ordinal);

    /// <summary>Gets whether this row is one a discovery may still be attributed to.</summary>
    /// <remarks>
    /// <para>
    /// A confirmed append, and nothing else. An issued one names no copy anybody can point at, and a withdrawn one is
    /// not at <see cref="OutgoingMailFilingStage.Confirmed" /> either, so the copy that has been taken back out of the
    /// folder answers for no discovery made after it went.
    /// </para>
    /// <para>
    /// Having been met once does not close the row, because one message can genuinely occur twice in the same folder:
    /// a provider that files the sent copy itself puts its own beside the one this system appended, and both are the
    /// user's own outgoing message. Attributing only the first would leave the second scored, classified, and reacted
    /// to as mail somebody sent them. The identity being compared is the one MailFathom minted and is unguessable, so
    /// a second match is another occurrence of the same message rather than a different message resembling it.
    /// </para>
    /// </remarks>
    private bool IsJoinable => this.Stage == OutgoingMailFilingStage.Confirmed;

    private bool NamesFolder(RemoteFolderPath discoveredFolderPath) =>
        this.FolderPath.NamesSameFolderAs(discoveredFolderPath);
}
