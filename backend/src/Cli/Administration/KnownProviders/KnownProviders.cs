// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.KnownProviders;

/// <summary>One page of the providers whose receiving server an account believes by default, as the deployment lists them.</summary>
/// <param name="Providers">The page's entries, in the order they were added.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> when this page is the last.</param>
internal sealed record KnownProviderList(
    [property: JsonPropertyName("providers")] IReadOnlyList<KnownProviderEntry>? Providers,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>One entry as a listing reports it.</summary>
/// <param name="ImapHost">The IMAP host accounts synchronize from.</param>
/// <param name="AuthorityIdentifier">The authserv-id an account on that host believes.</param>
/// <param name="AddedBy">Who added it.</param>
/// <param name="AddedAt">When it was added.</param>
internal sealed record KnownProviderEntry(
    [property: JsonPropertyName("imapHost")] string? ImapHost,
    [property: JsonPropertyName("authorityIdentifier")] string? AuthorityIdentifier,
    [property: JsonPropertyName("addedBy")] string? AddedBy,
    [property: JsonPropertyName("addedAt")] DateTimeOffset AddedAt);

/// <summary>An entry to add.</summary>
/// <param name="ImapHost">The IMAP host, as the operator typed it.</param>
/// <param name="AuthorityIdentifier">The authserv-id, as the operator typed it.</param>
internal sealed record KnownProviderRequest(
    [property: JsonPropertyName("imapHost")] string ImapHost,
    [property: JsonPropertyName("authorityIdentifier")] string AuthorityIdentifier);

/// <summary>An entry as the deployment added it, in the form it is matched by.</summary>
/// <param name="ImapHost">The IMAP host.</param>
/// <param name="AuthorityIdentifier">The authserv-id.</param>
internal sealed record KnownProviderAdded(
    [property: JsonPropertyName("imapHost")] string? ImapHost,
    [property: JsonPropertyName("authorityIdentifier")] string? AuthorityIdentifier);
