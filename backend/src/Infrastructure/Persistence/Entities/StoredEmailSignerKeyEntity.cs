// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The certificate or OpenPGP public key that signed one stored email, kept as key material mail can later be encrypted to.</summary>
/// <remarks>
/// <para>
/// A table of its own for the reason raw MIME and the search document are: it is binary, it can reach tens of kilobytes,
/// and every ordinary mailbox query that loads a stored email would otherwise carry it through the change tracker. No
/// read projection selects it and nothing publishes it.
/// </para>
/// <para>
/// It names the person who signed, so it inherits the message's classification, retention, export, and erasure
/// obligations whole, and the cascade from the stored email is what makes deleting the message delete it.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailSignerKeyEntity
{
    /// <summary>Gets or sets the stored email whose signer this is, which is also the row's identity.</summary>
    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets the stored email whose signer this is.</summary>
    public StoredEmailEntity StoredEmail { get; set; } = null!;

    /// <summary>Gets or sets the certificate's DER encoding or the key's transferable public key packets.</summary>
    /// <remarks>Bounded by the domain value to a size no certificate or public key reaches.</remarks>
    public required byte[] KeyMaterial { get; set; }
}
