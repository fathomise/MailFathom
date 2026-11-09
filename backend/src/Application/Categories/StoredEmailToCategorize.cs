// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Categories;

/// <summary>One stored message a run over stored mail may ask about, and what it already holds.</summary>
/// <param name="Email">The message and the opening of the conversation it replies in.</param>
/// <param name="Records">The categories it already holds.</param>
public sealed record StoredEmailToCategorize(EmailAwaitingCategorization Email, IReadOnlyList<EmailCategoryRecord> Records);
