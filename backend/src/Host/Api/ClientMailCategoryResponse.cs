// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Categories;

namespace MailFathom.Host.Api;

/// <summary>One category a message or a conversation shows.</summary>
/// <param name="Identity">A default's published identity, which a client names in its own language, or an own category's name.</param>
/// <param name="Name">The name an account gave its own category, drawn as written, or <see langword="null" /> for a default.</param>
/// <remarks>Derived personal data under the same rules as the row carrying it, and a model's reading of the mail rather than evidence about who sent it.</remarks>
internal sealed record ClientMailCategoryResponse(string Identity, string? Name)
{
    /// <summary>Describes what a message or a conversation shows, keeping its order.</summary>
    /// <param name="shown">The categories its account shows, each a default's published identity or an own category's name.</param>
    /// <returns>The response body, empty where it shows none.</returns>
    /// <remarks>An own category cannot take a default's identity, because the account's record refuses that name, so which of the two a value is follows from the value.</remarks>
    internal static IReadOnlyList<ClientMailCategoryResponse> For(IReadOnlyList<string> shown) =>
        [.. shown.Select(static category => new ClientMailCategoryResponse(
            category,
            DefaultMailCategory.TryParse(category, out _) ? null : category))];
}
