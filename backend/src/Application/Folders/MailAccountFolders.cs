// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Tracking;

namespace MailFathom.Application.Folders;

/// <summary>One of the user's accounts together with the folders a screen draws beneath it.</summary>
/// <param name="Account">How current the account's local copy is, and under which names it is published.</param>
/// <param name="Folders">One entry per folder local state knows of, ordered by alias, empty when synchronization has reached none.</param>
/// <param name="Outbox">What the account's outbox holds, or <see langword="null" /> for a caller that may not read the outbox.</param>
/// <remarks>
/// <para>
/// The account travels with its folders rather than being left to a second request, because a tree is one thing on
/// screen: a client that read the folders here and the mailbox names elsewhere would be composing one picture out of
/// two answers, the second of them already stale relative to the first.
/// </para>
/// <para>
/// The outbox is a member of the account rather than one of its folders, and its count travels here for the same
/// reason the folders do: it is what a client decides whether to draw the outbox from. A caller without both grants
/// an outbox is read under gets no count at all rather than zero, so it is not told the outbox's size either.
/// </para>
/// </remarks>
public sealed record MailAccountFolders(
    MailAccountFreshness Account,
    IReadOnlyList<DescribedMailFolder> Folders,
    AccountOutboxCount? Outbox = null);
