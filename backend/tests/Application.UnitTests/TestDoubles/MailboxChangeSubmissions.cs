// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Signals;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Builds the change submission a use case writes through.</summary>
/// <remarks>
/// Left to its defaults, no account has a custody state, which resolves to the mirrored custodian, so every change
/// submitted through it is written down as a record. A test about a held or a restoring account hands in the
/// <see cref="InMemoryLocalMailFolderStore" /> that says so, whose custody the submission's resolver then reads.
/// </remarks>
internal static class MailboxChangeSubmissions
{
    internal static MailboxChangeSubmission Over(
        IMailboxMutationRecordStore records,
        ILocalMailFolderStore? localFolders = null,
        ILocalEmailStateStore? states = null,
        LocalMailCopier? copier = null,
        IMailboxMutationAuditEntryStore? auditEntries = null,
        IMailboxMutationAuditSettingsReader? auditSettings = null,
        ClientSignals? signals = null,
        TimeProvider? timeProvider = null) =>
        Composing(records, localFolders, states, copier, auditEntries, auditSettings, signals, timeProvider).Submission;

    internal static MailboxCustodianComposition Composing(
        IMailboxMutationRecordStore records,
        ILocalMailFolderStore? localFolders = null,
        ILocalEmailStateStore? states = null,
        LocalMailCopier? copier = null,
        IMailboxMutationAuditEntryStore? auditEntries = null,
        IMailboxMutationAuditSettingsReader? auditSettings = null,
        ClientSignals? signals = null,
        TimeProvider? timeProvider = null)
    {
        var defaults = new MailboxCustodianComposition(timeProvider ?? new FakeTimeProvider());

        return new MailboxCustodianComposition(defaults.Clock)
        {
            Records = records,
            LocalFolders = localFolders ?? defaults.LocalFolders,
            Custody = localFolders is InMemoryLocalMailFolderStore inMemory ? inMemory.Custody : defaults.Custody,
            States = states ?? defaults.States,
            Copier = copier ?? LocalMailCopiers.Over(localFolders ?? defaults.LocalFolders),
            AuditEntries = auditEntries ?? defaults.AuditEntries,
            AuditSettings = auditSettings ?? defaults.AuditSettings,
            Signals = signals ?? defaults.Signals,
        };
    }
}
