// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Emails.Settlement;

/// <summary>Everything the settlement decides one arriving message from, and nothing that is mail.</summary>
/// <remarks>
/// Where the message is, when it was stored, whether its content has been read, what classification concluded, whether
/// the rules have finished with it, and whether a move a rule or a verdict asked for is still on its way — so a message is settled without being opened
/// and a release is reported without describing it.
/// </remarks>
/// <param name="Id">The stored message.</param>
/// <param name="AccountId">The account it belongs to.</param>
/// <param name="FolderAlias">The alias of the folder binding it is under, which the spam terms are named by.</param>
/// <param name="StoredAt">When it was stored, which every wait is measured from.</param>
/// <param name="ContentAvailability">
/// Whether its content has been read — which is when its sender's authentication, its trust, and its signature were
/// established — and whether a classification has anything to read.
/// </param>
/// <param name="Verdict">What classification concluded, or <see langword="null" /> while nothing has.</param>
/// <param name="AreRulesFinished">Whether a rule pass has evaluated it, or it is a copy no rule may act on.</param>
/// <param name="IsTombstoned">Whether its source occurrence has gone, which no rule pass reaches.</param>
/// <param name="AwaitsPlacement">Whether a move or a delete a rule or a verdict asked for has not reached the stored row yet.</param>
/// <param name="IsSeen">Whether the source already marked it read, which makes it nothing new to announce.</param>
/// <param name="ArrivedInInbox">Whether synchronization stored it into the account's inbox.</param>
/// <param name="FolderRole">The role of the folder it is in now, or <see langword="null" /> for a folder with none.</param>
public sealed record SettlingEmail(
    StoredEmailId Id,
    MailAccountId AccountId,
    MailFolderAlias FolderAlias,
    DateTimeOffset StoredAt,
    StoredEmailContentAvailability ContentAvailability,
    SpamVerdict? Verdict,
    bool AreRulesFinished,
    bool IsTombstoned,
    bool AwaitsPlacement,
    bool IsSeen,
    bool ArrivedInInbox,
    MailFolderSpecialUse? FolderRole)
{
    /// <summary>Decides whether the message settles now, and how.</summary>
    /// <param name="terms">The spam terms in force, read once for the pass.</param>
    /// <param name="releasedWhenStoredBefore">The instant a message stored earlier settles whatever it still waits on.</param>
    /// <returns>The settlement, or <see cref="MailSettlement.Settling" /> while it still waits.</returns>
    /// <remarks>
    /// <para>
    /// A message whose content is waiting for storage headroom waits with it, because its sender's authentication, its
    /// trust, and its signature are established when the content is read, and a reader shown it first would see a
    /// message from an unverified sender that is verified a run later.
    /// </para>
    /// <para>
    /// Spam comes first because the rules wait on it: a message with no verdict is not offered to a rule, and a message
    /// judged junk never will be, so the rules count as finished for it. A verdict that files the message, and a rule that
    /// moves it, both leave a move on its way, and the message waits for it to reach the row — which is what lets a reader
    /// see it once, in the folder it ends up in.
    /// </para>
    /// <para>
    /// Every wait is bounded. A verdict is released on the classification's own terms, and anything else a message waits
    /// on — a stalled rule pass, a move that never reaches the row — is released once the message has waited longer than
    /// <paramref name="releasedWhenStoredBefore" /> allows. A late message is shown late rather than never.
    /// </para>
    /// </remarks>
    public MailSettlement DecideUnder(DerivedWorkAdmissionTerms terms, DateTimeOffset releasedWhenStoredBefore)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var admission = DerivedWorkGate.Admit(
            terms,
            new DerivedWorkCandidate(this.AccountId, this.FolderAlias, this.StoredAt, this.ContentAvailability, this.Verdict));
        var rulesWillNeverRun = admission is DerivedWorkAdmission.WithheldAsJunk || this.IsTombstoned;
        var isWaiting = this.ContentAvailability is StoredEmailContentAvailability.AwaitingStorageHeadroom
            || admission is DerivedWorkAdmission.AwaitingClassification
            || (!this.AreRulesFinished && !rulesWillNeverRun)
            || this.AwaitsPlacement;

        if (isWaiting)
        {
            return this.StoredAt <= releasedWhenStoredBefore
                ? MailSettlement.ReleasedAfterWaiting
                : MailSettlement.Settling;
        }

        return admission switch
        {
            DerivedWorkAdmission.ReleasedAsUnclassifiable => MailSettlement.ReleasedAsUnclassifiable,
            DerivedWorkAdmission.ReleasedAfterWaiting => MailSettlement.ReleasedWithoutVerdict,
            _ => MailSettlement.Settled,
        };
    }

    /// <summary>Reports whether the message's arrival is announced to its user once it settles.</summary>
    /// <returns>
    /// <see langword="true" /> for an unread message in the inbox, and for one that reached the inbox and was moved on into a
    /// folder other than junk or trash; never for junk, for mail in junk or trash, or for mail that arrived elsewhere.
    /// </returns>
    /// <remarks>
    /// Nothing but a rule moves a settling message out of the inbox into an ordinary folder — a verdict files into junk,
    /// and no reader has seen the message yet to move it — so an inbox arrival settled elsewhere is one the user's own
    /// rule filed.
    /// </remarks>
    public bool IsAnnounced() =>
        !this.IsTombstoned
        && !this.IsSeen
        && this.Verdict is not SpamVerdict.Spam
        && (this.FolderRole is MailFolderSpecialUse.Inbox
            || (this.ArrivedInInbox && this.FolderRole is not (MailFolderSpecialUse.Junk or MailFolderSpecialUse.Trash)));
}
