// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Categories;

/// <summary>One arriving message a categorization pass will ask about.</summary>
/// <param name="Id">The stored message.</param>
/// <param name="ConversationOpening">
/// The opening of the conversation it replies in, or <see langword="null" /> where it replies to nothing stored here, or
/// where that first message is one no model may be shown.
/// </param>
public sealed record EmailAwaitingCategorization(StoredEmailId Id, ConversationOpening? ConversationOpening);
