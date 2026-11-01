// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>One row of the conversation list, with the messages it quotes described.</summary>
/// <param name="Thread">The conversation as the list read it, its participants cut to the named bound and its title guarded.</param>
/// <param name="MoreParticipantsNotNamed">Whether more people wrote into it than <see cref="IEmailThreadListReader.MaximumNamedParticipants" />.</param>
/// <param name="Newest">Its newest message, which the row quotes.</param>
/// <param name="NewestPreview">The opening of that message's own text, or <see langword="null" /> where nothing has extracted it.</param>
/// <param name="NewestReceivedSender">What was established about who sent its newest received message, which the row's sender mark is chosen from, or <see langword="null" /> where the reader wrote every message.</param>
public sealed record BrowsedListedThread(
    ListedEmailThread Thread,
    bool MoreParticipantsNotNamed,
    EmailSummary Newest,
    string? NewestPreview,
    SenderVerification? NewestReceivedSender)
{
    /// <summary>Gets whether the reader wrote the newest message.</summary>
    public bool NewestIsReaders => this.Thread.NewestReceivedEmailId != this.Thread.NewestEmailId;
}
