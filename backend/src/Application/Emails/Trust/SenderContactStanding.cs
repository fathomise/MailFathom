// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Trust;

/// <summary>What one message's sender is to the person reading it: whose contact recognized them, and whether the reader may write them down.</summary>
/// <remarks>
/// Unlike the verdict beside it, this is not stored with the message. It is asked per reader on each read, because two
/// users of one mailbox read one verdict and hold different books.
/// </remarks>
public sealed record SenderContactStanding
{
    /// <summary>Gets the standing of a message nothing is said about to this reader.</summary>
    public static SenderContactStanding Unanswered { get; } = new();

    /// <summary>Gets whose book holds the personal contact that recognized the author, or <see langword="null" /> where no personal contact did.</summary>
    public RecognizingContactHolder? RecognizingContactHolder { get; init; }

    /// <summary>Gets how the reader may write the sender down in their own book, or <see langword="null" /> where that is not offered.</summary>
    public ContactAdditionOffer? ContactAddition { get; init; }
}
