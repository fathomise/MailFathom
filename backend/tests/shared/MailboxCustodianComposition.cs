// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using NSubstitute;

namespace MailFathom.TestSupport;

/// <summary>Composes the mailbox custodians, their resolver, and every collaborator that routes an act through them.</summary>
/// <remarks>
/// <para>
/// The custodians and the collaborators that ask the resolver for one depend on each other, which the container breaks
/// with a lazily resolved custodian. This does the same, so a test builds the graph a deployment runs instead of a
/// second wiring of it: every part is built on first use, from the ports as they stand at that moment.
/// </para>
/// <para>
/// Every port a test does not state is a substitute. Left to its defaults the custody store answers no state for any
/// account, which resolves to the mirrored custodian, so a test about a held or a restoring account states the custody
/// store that says so. Set the ports in the object initializer, before the first part is read: a part already built
/// keeps the ports it was built over.
/// </para>
/// </remarks>
internal sealed class MailboxCustodianComposition
{
    /// <summary>Initializes the composition over the clock every part mints and measures with.</summary>
    /// <param name="clock">The clock.</param>
    internal MailboxCustodianComposition(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        this.Clock = clock;
        this.AuditSettings.GetAuditSettings(Arg.Any<MailAccountId>()).Returns(MailboxMutationAuditSettings.Disabled);
    }

    /// <summary>Gets the clock every part mints and measures with.</summary>
    internal TimeProvider Clock { get; }

    /// <summary>Gets the store the resolver reads each account's custody phase from.</summary>
    internal IMailAccountCustodyStore Custody { get; init; } = Substitute.For<IMailAccountCustodyStore>();

    /// <summary>Gets the account's own folders and the placement of its mail in them.</summary>
    internal ILocalMailFolderStore LocalFolders { get; init; } = Substitute.For<ILocalMailFolderStore>();

    /// <summary>Gets where a change owed to the source is written down.</summary>
    internal IMailboxMutationRecordStore Records { get; init; } = Substitute.For<IMailboxMutationRecordStore>();

    /// <summary>Gets each stored message's local state.</summary>
    internal ILocalEmailStateStore States { get; init; } = Substitute.For<ILocalEmailStateStore>();

    /// <summary>Gets the stored messages.</summary>
    internal IEmailMetadataRepository Emails { get; init; } = Substitute.For<IEmailMetadataRepository>();

    /// <summary>Gets the stored payloads.</summary>
    internal IEmailContentStore Contents { get; init; } = Substitute.For<IEmailContentStore>();

    /// <summary>Gets the reader a filed or copied payload's metadata comes from.</summary>
    internal IEmailMimeReader MimeReader { get; init; } = Substitute.For<IEmailMimeReader>();

    /// <summary>Gets the audit settings, which default to auditing nothing.</summary>
    internal IMailboxMutationAuditSettingsReader AuditSettings { get; init; } = Substitute.For<IMailboxMutationAuditSettingsReader>();

    /// <summary>Gets where an audited change is written.</summary>
    internal IMailboxMutationAuditEntryStore AuditEntries { get; init; } = Substitute.For<IMailboxMutationAuditEntryStore>();

    /// <summary>Gets the publisher every part announces through.</summary>
    internal ClientSignals Signals { get; init; } = ClientSignalPublishers.ReachingNobody;

    /// <summary>Gets the account's folder mappings.</summary>
    internal IMailFolderMappingReader Mappings { get; init; } = Substitute.For<IMailFolderMappingReader>();

    /// <summary>Gets the bindings of mapped folders to the source.</summary>
    internal IMailFolderResolutionStore FolderResolutions { get; init; } = Substitute.For<IMailFolderResolutionStore>();

    /// <summary>Gets the policy every connection to the source is opened under.</summary>
    internal IMailTransportSecurityPolicyReader TransportPolicies { get; init; } = Substitute.For<IMailTransportSecurityPolicyReader>();

    /// <summary>Gets the source's write sessions.</summary>
    internal IMailboxWriteSessionFactory WriteSessions { get; init; } = Substitute.For<IMailboxWriteSessionFactory>();

    /// <summary>Gets the source's folder listing.</summary>
    internal IRemoteFolderCatalog RemoteFolderCatalog { get; init; } = Substitute.For<IRemoteFolderCatalog>();

    /// <summary>Gets what creates a folder on the source.</summary>
    internal IRemoteFolderCreator RemoteFolderCreator { get; init; } = Substitute.For<IRemoteFolderCreator>();

    /// <summary>Gets what renames, moves, and deletes a folder on the source.</summary>
    internal IRemoteFolderEditor RemoteFolderEditor { get; init; } = Substitute.For<IRemoteFolderEditor>();

    /// <summary>Gets where a folder a user made on the source is declared.</summary>
    internal IMailFolderDeclarationWriter Declarations { get; init; } = Substitute.For<IMailFolderDeclarationWriter>();

    /// <summary>Gets what a deleted mirrored folder's mail is done with.</summary>
    internal IAuthoredFolderDeleteDispositionReader DeleteDispositions { get; init; } = Substitute.For<IAuthoredFolderDeleteDispositionReader>();

    /// <summary>Gets where a change to a folder mapping is audited.</summary>
    internal IMailFolderMappingChangeAuditor MappingChangeAuditor { get; init; } = Substitute.For<IMailFolderMappingChangeAuditor>();

    /// <summary>Gets where a change to a local folder is audited.</summary>
    internal ILocalMailFolderChangeAuditor FolderChangeAuditor { get; init; } = Substitute.For<ILocalMailFolderChangeAuditor>();

    /// <summary>Gets where a folder act's follow-up work is queued.</summary>
    internal IJobStore Jobs { get; init; } = Substitute.For<IJobStore>();

    /// <summary>Gets the authorization a folder act is checked against, which grants nothing by default.</summary>
    internal AccessAuthorization Authorization { get; init; } = AccessAuthorizations.ForCallerGranted();

    /// <summary>Gets the transactions every part commits in.</summary>
    internal IPersistenceSessionFactory PersistenceSessions { get; init; } = Substitute.For<IPersistenceSessionFactory>();

    /// <summary>Gets the drafts.</summary>
    internal IMailDraftStore Drafts { get; init; } = Substitute.For<IMailDraftStore>();

    /// <summary>Gets the account's sends.</summary>
    internal IOutgoingEmailStore OutgoingEmails { get; init; } = Substitute.For<IOutgoingEmailStore>();

    /// <summary>Gets whether an account files a copy of what it sends.</summary>
    internal IOutgoingMailFilingPolicyReader FilingPolicies { get; init; } = Substitute.For<IOutgoingMailFilingPolicyReader>();

    /// <summary>Gets what was written down about each filed copy.</summary>
    internal IOutgoingMailFilingStore Filings { get; init; } = Substitute.For<IOutgoingMailFilingStore>();

    /// <summary>Gets the outbox's bounds, which the filing pass reads its batch size from.</summary>
    internal MailOutboxSettings OutboxSettings { get; init; } = MailOutboxSettings.Create(
        maxDeliveriesPerPass: 10,
        leaseDuration: TimeSpan.FromMinutes(5),
        attemptTimeout: TimeSpan.FromMinutes(1),
        maxAttempts: 3,
        retryBaseDelay: TimeSpan.FromMinutes(1),
        retryMaxDelay: TimeSpan.FromMinutes(10),
        allowedLateness: TimeSpan.FromDays(1));

    /// <summary>Gets the resolver, which is the one reader of an account's custody phase for an act.</summary>
    internal MailboxCustodianResolver Custodians => field ??= new(
        this.Custody,
        new Lazy<MirroredMailboxCustodian>(() => this.Mirrored),
        new Lazy<HeldMailboxCustodian>(() => this.Held),
        new Lazy<RestoringMailboxCustodian>(() => this.Restoring));

    /// <summary>Gets the custodian of an account whose source is the truth.</summary>
    internal MirroredMailboxCustodian Mirrored => field ??= new(
        this.MirroredFolderEditor,
        this.LocalFolders,
        this.Records,
        this.OnDemandDestinations,
        this.DraftFiler,
        this.OutgoingFilings);

    /// <summary>Gets the custodian of an account MailFathom holds alone.</summary>
    internal HeldMailboxCustodian Held => field ??= new(
        this.LocalFolderEditor,
        this.Changes,
        this.LocalFiler,
        this.DraftFiler);

    /// <summary>Gets the custodian of an account whose mailbox is being restored to its source.</summary>
    internal RestoringMailboxCustodian Restoring => field ??= new(
        this.LocalFolders,
        this.Changes,
        this.OnDemandDestinations,
        this.DraftFiler,
        this.OutgoingFilings);

    /// <summary>Gets the change submission every author of a change on a message writes through.</summary>
    internal MailboxChangeSubmission Submission => field ??= new(
        this.Custodians,
        this.Records,
        this.States,
        this.AuditEntries,
        this.Signals,
        this.Clock);

    /// <summary>Gets what commits a change on a message to stored state.</summary>
    internal LocalMailboxChanges Changes => field ??= new(
        this.LocalFolders,
        this.Records,
        this.States,
        this.Copier,
        this.AuditSettings,
        this.AuditEntries,
        this.Clock);

    /// <summary>Gets what copies a stored message locally, which a test about copying states itself.</summary>
    internal LocalMailCopier Copier
    {
        get => field ??= new(this.Emails, this.Contents, this.MimeReader, this.LocalFolders);
        init => field = value;
    }

    /// <summary>Gets what edits a held account's own folders.</summary>
    internal LocalMailFolderEditor LocalFolderEditor => field ??= new(
        this.LocalFolders,
        this.Custodians,
        this.CommitPolicy,
        this.FolderChangeAuditor,
        this.Signals,
        this.Jobs,
        this.Authorization,
        this.Clock);

    /// <summary>Gets what edits a mirrored account's folders on its source.</summary>
    internal MirroredMailFolderEditor MirroredFolderEditor => field ??= new(
        this.Mappings,
        this.FolderResolutions,
        this.Declarations,
        this.RemoteFolderCreator,
        this.RemoteFolderEditor,
        this.TransportPolicies,
        this.DeleteDispositions,
        this.Signals,
        this.Jobs,
        this.Authorization,
        this.FolderResolver);

    /// <summary>Gets what binds a mapped folder to the source.</summary>
    internal MailFolderResolver FolderResolver => field ??= new(
        this.RemoteFolderCatalog,
        this.RemoteFolderCreator,
        this.FolderResolutions,
        this.MappingChangeAuditor,
        this.PersistenceSessions,
        this.Signals,
        this.Clock);

    /// <summary>Gets what turns a named destination into the folder a change is issued against.</summary>
    internal MailboxDestinationResolver Destinations => field ??= new(
        new MailFolderReferenceResolver(this.Mappings),
        this.FolderResolutions,
        this.Custodians);

    /// <summary>Gets what resolves a destination the account maps but does not synchronize against the source.</summary>
    internal OnDemandMailboxDestinationResolver OnDemandDestinations => field ??= new(this.FolderResolver, this.TransportPolicies);

    /// <summary>Gets what builds the local filer over the resolver, for a test whose held account files through ports of its own.</summary>
    internal Func<MailboxCustodianResolver, LocalMailFiler>? LocalFilerOver { get; init; }

    /// <summary>Gets what files a held account's draft revision or sent copy locally.</summary>
    internal LocalMailFiler LocalFiler => field ??= this.LocalFilerOver?.Invoke(this.Custodians) ?? new(
        this.LocalFolders,
        this.Custodians,
        this.Emails,
        this.Contents,
        this.MimeReader,
        this.Mappings,
        this.FolderResolutions,
        this.FilingPolicies,
        this.Filings,
        this.Signals,
        this.Clock);

    /// <summary>Gets what files a draft's revisions.</summary>
    internal MailDraftFiler DraftFiler => field ??= new(
        this.Appender,
        this.LocalFiler,
        this.Custodians,
        this.WriteSessions,
        this.Destinations,
        this.Drafts,
        this.TransportPolicies,
        this.CommitPolicy,
        this.Clock);

    /// <summary>Gets what appends a copy to the source.</summary>
    internal MailboxCopyAppender Appender => field ??= new(
        this.WriteSessions,
        this.Destinations,
        this.Contents,
        this.TransportPolicies,
        this.Clock);

    /// <summary>Gets what appends and withdraws one server copy of a send.</summary>
    internal OutgoingMailFiler OutgoingFiler => field ??= new(
        this.Appender,
        this.WriteSessions,
        this.Destinations,
        this.Filings,
        this.TransportPolicies,
        this.CommitPolicy,
        this.Clock);

    /// <summary>Gets the pass that keeps the server copies of the account's sends in step.</summary>
    internal OutgoingMailFilingPass OutgoingFilings => field ??= new(
        this.OutgoingEmails,
        this.OutgoingFiler,
        this.FilingPolicies,
        this.Filings,
        this.Custodians,
        this.Clock,
        this.OutboxSettings);

    /// <summary>Gets the policy every part retries a lost concurrency race with, which a test racing a commit states itself.</summary>
    internal OptimisticConcurrencyRetryPolicy CommitPolicy
    {
        get => field ??= new(this.PersistenceSessions, new PersistenceConcurrencyOptions(), this.Clock);
        init => field = value;
    }
}
