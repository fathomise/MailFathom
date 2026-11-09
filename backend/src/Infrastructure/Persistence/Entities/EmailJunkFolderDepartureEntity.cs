// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>That a message left its account's junk folder through another mail client, kept long enough to recognize where it lands.</summary>
/// <remarks>
/// A table of its own rather than a mark on the stored email, because under the default disposition that row is erased
/// before the arrival it would be compared with has been stored. It holds a digest and an instant and nothing the message
/// supplied that can be read back.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailJunkFolderDepartureEntity
{
    public string MailboxAccountId { get; set; } = string.Empty;

    /// <summary>Gets or sets the digest of the message's <c>Message-ID</c>, the one thing another client's move leaves the two copies sharing.</summary>
    public string MessageIdDigest { get; set; } = string.Empty;

    public DateTimeOffset DepartedAt { get; set; }

    /// <summary>Computes the digest a <c>Message-ID</c> is recorded and compared under.</summary>
    /// <param name="internetMessageId">The identifier as the row stores it.</param>
    /// <returns>Its SHA-256, in lower-case hexadecimal.</returns>
    internal static string DigestOf(string internetMessageId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(internetMessageId)));
}
