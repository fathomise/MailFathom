// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.KnownAuthenticationProviders;

namespace MailFathom.Host.Api;

/// <summary>An entry an administrator asks to add to the known authentication providers.</summary>
/// <param name="ImapHost">The IMAP host accounts synchronize from.</param>
/// <param name="AuthorityIdentifier">The authserv-id that provider's receiving server writes.</param>
internal sealed record KnownAuthenticationProviderRequest(string? ImapHost, string? AuthorityIdentifier);

/// <summary>An entry as it was added, in the form it is matched by.</summary>
/// <param name="ImapHost">The IMAP host, lower-cased and without a trailing dot.</param>
/// <param name="AuthorityIdentifier">The authserv-id, lower-cased.</param>
internal sealed record KnownAuthenticationProviderAddedResponse(string ImapHost, string AuthorityIdentifier);

/// <summary>One page of the known authentication providers, in the order they were added.</summary>
/// <param name="Providers">The page's entries.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> when this page is the last.</param>
internal sealed record KnownAuthenticationProviderListResponse(
    IReadOnlyList<KnownAuthenticationProviderResponse> Providers,
    string? NextCursor);

/// <summary>One entry as a listing reports it.</summary>
/// <param name="ImapHost">The IMAP host.</param>
/// <param name="AuthorityIdentifier">The authserv-id an account on that host believes.</param>
/// <param name="AddedBy">Who added it, naming the user and the credential, or the migration that seeded it.</param>
/// <param name="AddedAt">When it was added.</param>
internal sealed record KnownAuthenticationProviderResponse(
    string ImapHost,
    string AuthorityIdentifier,
    string AddedBy,
    DateTimeOffset AddedAt)
{
    /// <summary>Maps an entry to its transport shape.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The response.</returns>
    internal static KnownAuthenticationProviderResponse For(ListedKnownAuthenticationProvider entry) => new(
        entry.ImapHost,
        entry.AuthorityIdentifier,
        entry.AddedBy,
        entry.AddedAt);
}
