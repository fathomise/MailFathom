// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Categories;

namespace MailFathom.Application.Categories;

/// <summary>That a model said one message holds one category.</summary>
/// <param name="Kind">Which of the two kinds of question this answers.</param>
/// <param name="Category">A default's published identity or an own category's name.</param>
/// <param name="Probability">The probability the model gave it.</param>
/// <param name="ModelBuild">The build that answered.</param>
/// <param name="QuestionRevision">The revision of the question this answers.</param>
public sealed record EmailCategoryAssignment(
    EmailCategoryKind Kind,
    string Category,
    double Probability,
    string ModelBuild,
    string QuestionRevision);
