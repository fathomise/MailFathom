// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Builds the passages a retrieval hands over, so a test states only what it is about.</summary>
internal static class KnowledgePassages
{
    /// <summary>Gets the provenance of an authenticated author the mailbox has corresponded with before.</summary>
    public static EmailProvenance KnownCorrespondent => StubEmailProvenanceReader.KnownCorrespondent;

    /// <summary>Gets the provenance of a first-time sender nothing authenticated, sitting in the junk folder.</summary>
    public static EmailProvenance UnverifiedStranger => StubEmailProvenanceReader.UnverifiedStranger;

    /// <summary>Builds one passage.</summary>
    /// <param name="text">The extract itself.</param>
    /// <param name="storedEmailId">The stable identity, or <see langword="null" /> to generate one.</param>
    /// <param name="accountId">The account the message belongs to.</param>
    /// <param name="folderPath">The place in its account's hierarchy of the folder the message is in.</param>
    /// <param name="subject">The subject, or <see langword="null" /> for a message that carried none.</param>
    /// <param name="provenance">
    /// Where the message came from, or <see langword="null" /> for an authenticated correspondent the mailbox already
    /// knows, which is the provenance that asks a model to qualify nothing.
    /// </param>
    /// <param name="machineAuthorship">
    /// How much the message's own text read as machine written, or <see langword="null" /> for the stored default. It is
    /// stated because the reading reaches no provider, and a passage carrying the default could not tell that guarantee
    /// from a reading that was dropped.
    /// </param>
    /// <param name="attachmentExtracts">What the message's files contributed, which is nothing for most passages.</param>
    /// <returns>The passage.</returns>
    public static EmailKnowledgePassage Create(
        string text,
        Guid? storedEmailId = null,
        string accountId = "primary",
        string folderPath = "INBOX",
        string? subject = null,
        EmailProvenance? provenance = null,
        MachineAuthorshipAssessment? machineAuthorship = null,
        params EmailKnowledgeAttachmentExtract[] attachmentExtracts) => new()
        {
            AttachmentExtracts = attachmentExtracts,
            StoredEmailId = StoredEmailId.Create(storedEmailId ?? Guid.CreateVersion7()),
            AccountId = MailAccountId.Create(accountId),
            Folder = new StoredEmailFolder(MailFolderId.Create(Guid.CreateVersion7()), folderPath, Alias: null),
            Subject = subject,
            ReceivedAt = null,
            Provenance = provenance ?? KnownCorrespondent,
            MachineAuthorship = machineAuthorship ?? MachineAuthorshipAssessment.NotAssessed,
            Text = text,
        };
}
