// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Categories;

/// <summary>The revision each question is asked under now, so a stored answer can be told apart from one to the question as it stands.</summary>
/// <param name="Default">The revision of the default choice.</param>
/// <param name="Own">The revision of each own category's question, by the category's name.</param>
public sealed record EmailCategoryQuestionRevisions(string Default, IReadOnlyDictionary<string, string> Own);
