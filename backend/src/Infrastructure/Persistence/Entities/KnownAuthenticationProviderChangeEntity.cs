// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One addition to or removal from the known authentication providers, kept for good.</summary>
/// <remarks>
/// It is append-only: nothing amends or deletes a row, so it carries no concurrency token, and it holds the entry's
/// values rather than a reference to it, because a removal leaves no entry to refer to and the record of it is exactly
/// what an audit of the list looks for.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class KnownAuthenticationProviderChangeEntity
{
    /// <summary>The table these rows live in, named here because a composed statement and the seeding migration read it.</summary>
    internal const string TableName = "known_authentication_provider_changes";

    /// <summary>The act recorded for an entry that was added.</summary>
    internal const string Added = "added";

    /// <summary>The act recorded for an entry that was removed.</summary>
    internal const string Removed = "removed";

    /// <summary>The change's identifier, a version 7 value.</summary>
    public Guid Id { get; set; }

    /// <summary>The IMAP host of the entry the change was about.</summary>
    public required string ImapHost { get; set; }

    /// <summary>The authserv-id that entry named.</summary>
    public required string AuthorityIdentifier { get; set; }

    /// <summary>What happened: <see cref="Added" /> or <see cref="Removed" />.</summary>
    public required string Act { get; set; }

    /// <summary>The identity of whoever made the change, naming the user and the credential.</summary>
    public required string ChangedBy { get; set; }

    /// <summary>When the change was made.</summary>
    public DateTimeOffset ChangedAt { get; set; }
}
