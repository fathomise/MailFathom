// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class MailboxForgottenOccurrenceEntity
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the account the folder belongs to, copied from that folder so the record answers which mailbox it is about without a join.</summary>
    public required string MailboxAccountId { get; set; }

    public long MailFolderBindingId { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }
}
