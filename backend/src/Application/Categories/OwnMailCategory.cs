// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Categories;

/// <summary>A subject a mailbox names for itself.</summary>
/// <param name="Name">What the category is called, which is what its records name it by.</param>
/// <param name="Description">What a model reads to decide whether a message is about the subject.</param>
public sealed record OwnMailCategory(string Name, string Description);
