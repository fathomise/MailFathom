// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Domain.Mutations.Filing;

/// <summary>The append half of a message a person filed into a folder of another account they hold.</summary>
/// <remarks>
/// <para>
/// A mail server moves a message only between folders of one account, so filing one into another account is an append on
/// the destination and a removal from the source, each converged by its own account under
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>
/// axis H. This is the destination's half. It is a record of its own rather than a mailbox mutation record because it
/// names no occurrence: the message it carries has none on the destination until the server's <c>APPENDUID</c> answer
/// gives it one.
/// </para>
/// <para>
/// The removal half is an ordinary delete record on a mirrored source, named by <see cref="RemovalRecordId" />, which no
/// pass issues until this half is <see cref="CrossAccountFilingStage.Appended" />. A held source has no removal record of
/// its own: its row is hidden until the append lands and then erased, which <see cref="ErasesHeldSource" /> says.
/// </para>
/// </remarks>
public sealed record CrossAccountFilingRecord
{
    /// <summary>Gets the filing's identity.</summary>
    public required CrossAccountFilingId Id { get; init; }

    /// <summary>Gets the account the message is filed into, whose converger carries this half.</summary>
    public required MailAccountId DestinationAccountId { get; init; }

    /// <summary>Gets the new stored message the filing wrote on the destination account.</summary>
    public required StoredEmailId DestinationEmailId { get; init; }

    /// <summary>Gets where on the destination's server the message is appended.</summary>
    public required RemoteFolderPath DestinationFolderPath { get; init; }

    /// <summary>Gets the account the message was filed out of.</summary>
    public required MailAccountId SourceAccountId { get; init; }

    /// <summary>Gets the stored message the person filed, which shows again where the filing is given up.</summary>
    public required StoredEmailId SourceEmailId { get; init; }

    /// <summary>Gets the delete record on a mirrored source that waits for this half, or <see langword="null" /> on a held one.</summary>
    public required MailboxMutationRecordId? RemovalRecordId { get; init; }

    /// <summary>Gets whether the source is a held account whose row is erased once the append lands.</summary>
    public required bool ErasesHeldSource { get; init; }

    /// <summary>Gets how far the append has got.</summary>
    public required CrossAccountFilingStage Stage { get; init; }

    /// <summary>Gets when the filing was committed.</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>Gets when the stage last moved.</summary>
    public required DateTimeOffset StageChangedAt { get; init; }

    /// <summary>Gets whether the filing has ended, either way.</summary>
    public bool IsTerminal => this.Stage is CrossAccountFilingStage.Appended or CrossAccountFilingStage.Abandoned;
}
