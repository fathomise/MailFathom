// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class AutomaticReplyAnswerEntity
{
    /// <summary>The table these rows live in, named here because the composed statements write it.</summary>
    internal const string TableName = "automatic_reply_answers";

    /// <summary>The user whose reply answered.</summary>
    public Guid UserId { get; set; }

    /// <summary>The revision of the reply the sender was answered under.</summary>
    public Guid Revision { get; set; }

    /// <summary>The address answered, in its comparison form, so two spellings of one address are one sender.</summary>
    public required string SenderAddress { get; set; }

    /// <summary>When the sender was answered.</summary>
    public DateTimeOffset AnsweredAt { get; set; }
}
