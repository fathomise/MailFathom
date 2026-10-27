// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.KnownAuthenticationProviders;

/// <summary>One page of the known authentication providers.</summary>
/// <param name="Entries">The page's entries, in the order they were added.</param>
/// <param name="ContinuesAfter">The last entry's identifier when another page follows, or <see langword="null" /> on the last page.</param>
public sealed record KnownAuthenticationProviderListing(
    IReadOnlyList<ListedKnownAuthenticationProvider> Entries,
    Guid? ContinuesAfter);
