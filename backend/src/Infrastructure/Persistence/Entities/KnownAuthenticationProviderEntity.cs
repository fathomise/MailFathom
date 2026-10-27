// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One provider whose receiving server every account reading mail from its IMAP host believes by default.</summary>
/// <remarks>
/// Both values are stored in their matching form, lower-cased and without a trailing dot on the host, so a lookup is an
/// equality on the unique host index.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class KnownAuthenticationProviderEntity
{
    /// <summary>The table these rows live in, named here because a composed statement and the seeding migration read it.</summary>
    internal const string TableName = "known_authentication_providers";

    /// <summary>The longest host or authserv-id stored, which is the length of a domain name.</summary>
    internal const int MaximumValueLength = 253;

    /// <summary>The longest caller identity stored.</summary>
    internal const int MaximumChangedByLength = 256;

    /// <summary>The entry's identifier, a version 7 value, which orders a listing.</summary>
    public Guid Id { get; set; }

    /// <summary>The IMAP host, in its matching form.</summary>
    public required string ImapHost { get; set; }

    /// <summary>The authserv-id that provider's receiving server writes, lower-cased.</summary>
    public required string AuthorityIdentifier { get; set; }

    /// <summary>The identity of whoever added the entry, naming the user and the credential, or the migration that seeded it.</summary>
    public required string AddedBy { get; set; }

    /// <summary>When the entry was added.</summary>
    public DateTimeOffset AddedAt { get; set; }
}
