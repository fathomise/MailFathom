// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;

namespace MailFathom.Application.Categories;

/// <summary>Everything categorization reads about one message, and nothing more.</summary>
/// <remarks>
/// The minimised input the spam model stage reads, for the reasons ADR 0041 records, and for a reply one thing beside it:
/// <i>Thanks, that works</i> says nothing alone, so the opening of the conversation it belongs to travels with it.
/// </remarks>
/// <param name="Message">What is read of the message itself.</param>
/// <param name="ConversationOpening">A bounded excerpt of the conversation's first message, or <see langword="null" /> where there is none to send.</param>
public sealed record EmailCategoryEvidence(SpamModelEvidence Message, ConversationOpening? ConversationOpening);
