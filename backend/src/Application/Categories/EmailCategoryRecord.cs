// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Categories;

namespace MailFathom.Application.Categories;

/// <summary>That a stored message holds one category, as far as deciding whether to ask again needs to know.</summary>
/// <param name="Kind">Which of the two kinds of question it answers.</param>
/// <param name="Category">A default's published identity or an own category's name.</param>
/// <param name="QuestionRevision">The revision of the question it answers.</param>
public sealed record EmailCategoryRecord(EmailCategoryKind Kind, string Category, string QuestionRevision);
