// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Mutations.Filing;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>Persists the append half of a message filed into another account's folder.</summary>
/// <remarks>
/// The stored messages and the removal record it names are plain identifiers rather than foreign keys. A key that
/// cascaded would take this row with whichever of them went first, and the row is what holds the removal back: a
/// destination erased before its append landed has to be read as a filing to give up, never as no filing at all, or the
/// removal would be issued against the only copy.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class CrossAccountFilingEntity
{
    internal const int MaximumFolderPathLength = 512;

    public Guid Id { get; set; }

    public required string DestinationAccountId { get; set; }

    public Guid DestinationStoredEmailId { get; set; }

    public required string DestinationFolderPath { get; set; }

    public required string SourceAccountId { get; set; }

    public Guid SourceStoredEmailId { get; set; }

    public Guid? RemovalMutationId { get; set; }

    public bool ErasesHeldSource { get; set; }

    public CrossAccountFilingStage Stage { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public DateTimeOffset StageChangedAt { get; set; }

    public uint ConcurrencyVersion { get; set; }
}
