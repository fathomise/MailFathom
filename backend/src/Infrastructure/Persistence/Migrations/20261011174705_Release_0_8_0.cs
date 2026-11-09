// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <summary>The schema release 0.8.0 carries: 120 migrations, folded into one.</summary>
    /// <remarks>
    /// Written by <c>scripts/squash-migrations.sh</c>. It carries the identifier of the last migration it folds, so a
    /// database that recorded that one skips it, and Up runs each folded migration's Up in the order they were added
    /// while Down runs their Down in reverse. Each folded migration is kept below as it was written.
    /// </remarks>
    public partial class Release_0_8_0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Initial.Up(migrationBuilder);
            AddMailboxRefreshTokens.Up(migrationBuilder);
            AddSynchronizationCheckpointModSeq.Up(migrationBuilder);
            AddEmailChunks.Up(migrationBuilder);
            AddEmbeddingProfilesAndEmailEmbeddings.Up(migrationBuilder);
            AddMailboxMutations.Up(migrationBuilder);
            AddMailboxMutationSourceRemoval.Up(migrationBuilder);
            AddMailboxMutationObservations.Up(migrationBuilder);
            AddAuthoredDeleteLocalDisposition.Up(migrationBuilder);
            AddMailboxMutationAuditEntries.Up(migrationBuilder);
            AddEmbeddingProfileLifecycleUniqueIndex.Up(migrationBuilder);
            AddStoredEmailAwaitingContentIndex.Up(migrationBuilder);
            AddEmbeddingSpendPeriodsAndChunkedTextTruncation.Up(migrationBuilder);
            AddMailAnsweringAuditEntries.Up(migrationBuilder);
            AddMailRuleEvaluationState.Up(migrationBuilder);
            AddMailRuleExecutionHistory.Up(migrationBuilder);
            AddEmailSpamClassifications.Up(migrationBuilder);
            RenameRuleExecutionDestinationColumn.Up(migrationBuilder);
            AddSpamClassificationRuns.Up(migrationBuilder);
            AddJobs.Up(migrationBuilder);
            AddSensitiveContentDerivationStamp.Up(migrationBuilder);
            RestrictJobClaimIndexToClaimableStates.Up(migrationBuilder);
            AddStoredEmailStoredAt.Up(migrationBuilder);
            AddJobFailureRecord.Up(migrationBuilder);
            AddJobDeadLetterIndex.Up(migrationBuilder);
            AddMailRuleScheduleTrigger.Up(migrationBuilder);
            AddStoredEmailRemoteKeywords.Up(migrationBuilder);
            AddStoredEmailSenderAuthentication.Up(migrationBuilder);
            AddStoredEmailSenderTrust.Up(migrationBuilder);
            ReplaceSenderDomainAlignmentWithAuthorAuthentication.Up(migrationBuilder);
            AddStoredEmailDisplayedAuthorDomain.Up(migrationBuilder);
            AddMailRederivationPositions.Up(migrationBuilder);
            AddContacts.Up(migrationBuilder);
            AddOutgoingEmailRecord.Up(migrationBuilder);
            AddEmailThreads.Up(migrationBuilder);
            AddEmailThreadConcurrencyToken.Up(migrationBuilder);
            AddEmailThreadSurvivorForeignKey.Up(migrationBuilder);
            AddMailboxMutationFlagAndKeywordParameters.Up(migrationBuilder);
            AddStoredEmailMachineAuthorship.Up(migrationBuilder);
            AddJobEnqueuedTraceContext.Up(migrationBuilder);
            AddOutgoingEmailLease.Up(migrationBuilder);
            AddMailRederivationRuns.Up(migrationBuilder);
            AddOutgoingRecipientContact.Up(migrationBuilder);
            AddStoredEmailSenderAuthenticationSource.Up(migrationBuilder);
            AddOutgoingEmailFilings.Up(migrationBuilder);
            AddOutgoingEmailPeriodUsageIndex.Up(migrationBuilder);
            AddScheduledAndRecurringSends.Up(migrationBuilder);
            AddOutgoingEmailPrincipal.Up(migrationBuilder);
            AddMailDrafts.Up(migrationBuilder);
            AddMailDraftRecipientProvenance.Up(migrationBuilder);
            AddUserAccounts.Up(migrationBuilder);
            AddContentStorageBackendAndObjectLocator.Up(migrationBuilder);
            AddJobTurn.Up(migrationBuilder);
            BoundEmbeddingSpendAndStoredContentPerUser.Up(migrationBuilder);
            IndexObjectBackedContentAndRequireItsPayloadEmpty.Up(migrationBuilder);
            AddContactUser.Up(migrationBuilder);
            IndexContentObjectLocators.Up(migrationBuilder);
            AddContentMoveRun.Up(migrationBuilder);
            RetainDatabaseCopyUntilReleased.Up(migrationBuilder);
            AddRootSettings.Up(migrationBuilder);
            CarryUserBesideMailAccountReferences.Up(migrationBuilder);
            KeyMailAccountByUserAndIdentifier.Up(migrationBuilder);
            AddUserLabelAndDocumentMarker.Up(migrationBuilder);
            AddUserPasswordCredentials.Up(migrationBuilder);
            ResolveUserFacingCredentialsToUserRows.Up(migrationBuilder);
            AddStoredSecrets.Up(migrationBuilder);
            AddDraftSubjectAndStagedAttachments.Up(migrationBuilder);
            AddClientPreferences.Up(migrationBuilder);
            AddUserPortrait.Up(migrationBuilder);
            AddNotifications.Up(migrationBuilder);
            AddAttachmentTextAndAttachmentChunks.Up(migrationBuilder);
            AddEmailEnrichment.Up(migrationBuilder);
            BoundAndReportAttachmentDerivationSpend.Up(migrationBuilder);
            AddNotificationCause.Up(migrationBuilder);
            AddEmailThreadStates.Up(migrationBuilder);
            WidenFilingJoinIndexesToEveryConfirmedCopy.Up(migrationBuilder);
            IndexStandingFilingsByFilingAndAppend.Up(migrationBuilder);
            AddWorkLeases.Up(migrationBuilder);
            AddSpentClientAssertions.Up(migrationBuilder);
            AddDeploymentWideCeilingRows.Up(migrationBuilder);
            AddMailboxMutationWithdrawalHold.Up(migrationBuilder);
            DropUserDocumentMarker.Up(migrationBuilder);
            AddClientSignalTickets.Up(migrationBuilder);
            AddWorkLeaseReplica.Up(migrationBuilder);
            AddClientSessions.Up(migrationBuilder);
            MakeStoredEmailOccurrenceOptional.Up(migrationBuilder);
            AddUserEndpointSwitches.Up(migrationBuilder);
            MoveUserPortraitsIntoStoredFiles.Up(migrationBuilder);
            AddLocalMailFolders.Up(migrationBuilder);
            AddOrganizationsScopingPasswordLogins.Up(migrationBuilder);
            FileHeldAccountDraftsAndSentCopiesLocally.Up(migrationBuilder);
            MoveMailAccountsIntoRecordsOfTheirOwn.Up(migrationBuilder);
            HoldSpamAndSensitiveContentOnTheMailAccount.Up(migrationBuilder);
            KeyTheMailGraphByTheAccountIdentifier.Up(migrationBuilder);
            SplitContactBooksByHolder.Up(migrationBuilder);
            AddMailboxExports.Up(migrationBuilder);
            AddRequestedCustodyAndSourceDrain.Up(migrationBuilder);
            FlagOnlyAuthoredDeletes.Up(migrationBuilder);
            AddTasks.Up(migrationBuilder);
            AddCalendarEvents.Up(migrationBuilder);
            AddCalendarEventReminders.Up(migrationBuilder);
            JournalDiscoveryRuns.Up(migrationBuilder);
            AddTaskReminders.Up(migrationBuilder);
            StoreDiscoveryRunEventPayloadAsWritten.Up(migrationBuilder);
            AddAgentConversations.Up(migrationBuilder);
            AddMailboxMutationLocalChange.Up(migrationBuilder);
            AddMailboxRestoreAppends.Up(migrationBuilder);
            RecordAgentConversationTechnicalHistory.Up(migrationBuilder);
            IndexAgentConversationsForHistorySearch.Up(migrationBuilder);
            ArchiveAnAgentConversation.Up(migrationBuilder);
            GroupMailAccountsIntoOrganizations.Up(migrationBuilder);
            AddRolesGroupsAndRoleAssignments.Up(migrationBuilder);
            HoldMailAccountSettingsInColumns.Up(migrationBuilder);
            HoldMailGrantsOnUsers.Up(migrationBuilder);
            AdmitUsersOnTheAdministrativeEndpoint.Up(migrationBuilder);
            RewriteAdministratorRoleAsAPattern.Up(migrationBuilder);
            AddSettingsPolicies.Up(migrationBuilder);
            StateEndpointSwitchesHeldOffOnUserRecords.Up(migrationBuilder);
            StampEndpointSwitchesOnUserRecords.Up(migrationBuilder);
            SplitUserFacingPermissionsByDomain.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            SplitUserFacingPermissionsByDomain.Down(migrationBuilder);
            StampEndpointSwitchesOnUserRecords.Down(migrationBuilder);
            StateEndpointSwitchesHeldOffOnUserRecords.Down(migrationBuilder);
            AddSettingsPolicies.Down(migrationBuilder);
            RewriteAdministratorRoleAsAPattern.Down(migrationBuilder);
            AdmitUsersOnTheAdministrativeEndpoint.Down(migrationBuilder);
            HoldMailGrantsOnUsers.Down(migrationBuilder);
            HoldMailAccountSettingsInColumns.Down(migrationBuilder);
            AddRolesGroupsAndRoleAssignments.Down(migrationBuilder);
            GroupMailAccountsIntoOrganizations.Down(migrationBuilder);
            ArchiveAnAgentConversation.Down(migrationBuilder);
            IndexAgentConversationsForHistorySearch.Down(migrationBuilder);
            RecordAgentConversationTechnicalHistory.Down(migrationBuilder);
            AddMailboxRestoreAppends.Down(migrationBuilder);
            AddMailboxMutationLocalChange.Down(migrationBuilder);
            AddAgentConversations.Down(migrationBuilder);
            StoreDiscoveryRunEventPayloadAsWritten.Down(migrationBuilder);
            AddTaskReminders.Down(migrationBuilder);
            JournalDiscoveryRuns.Down(migrationBuilder);
            AddCalendarEventReminders.Down(migrationBuilder);
            AddCalendarEvents.Down(migrationBuilder);
            AddTasks.Down(migrationBuilder);
            FlagOnlyAuthoredDeletes.Down(migrationBuilder);
            AddRequestedCustodyAndSourceDrain.Down(migrationBuilder);
            AddMailboxExports.Down(migrationBuilder);
            SplitContactBooksByHolder.Down(migrationBuilder);
            KeyTheMailGraphByTheAccountIdentifier.Down(migrationBuilder);
            HoldSpamAndSensitiveContentOnTheMailAccount.Down(migrationBuilder);
            MoveMailAccountsIntoRecordsOfTheirOwn.Down(migrationBuilder);
            FileHeldAccountDraftsAndSentCopiesLocally.Down(migrationBuilder);
            AddOrganizationsScopingPasswordLogins.Down(migrationBuilder);
            AddLocalMailFolders.Down(migrationBuilder);
            MoveUserPortraitsIntoStoredFiles.Down(migrationBuilder);
            AddUserEndpointSwitches.Down(migrationBuilder);
            MakeStoredEmailOccurrenceOptional.Down(migrationBuilder);
            AddClientSessions.Down(migrationBuilder);
            AddWorkLeaseReplica.Down(migrationBuilder);
            AddClientSignalTickets.Down(migrationBuilder);
            DropUserDocumentMarker.Down(migrationBuilder);
            AddMailboxMutationWithdrawalHold.Down(migrationBuilder);
            AddDeploymentWideCeilingRows.Down(migrationBuilder);
            AddSpentClientAssertions.Down(migrationBuilder);
            AddWorkLeases.Down(migrationBuilder);
            IndexStandingFilingsByFilingAndAppend.Down(migrationBuilder);
            WidenFilingJoinIndexesToEveryConfirmedCopy.Down(migrationBuilder);
            AddEmailThreadStates.Down(migrationBuilder);
            AddNotificationCause.Down(migrationBuilder);
            BoundAndReportAttachmentDerivationSpend.Down(migrationBuilder);
            AddEmailEnrichment.Down(migrationBuilder);
            AddAttachmentTextAndAttachmentChunks.Down(migrationBuilder);
            AddNotifications.Down(migrationBuilder);
            AddUserPortrait.Down(migrationBuilder);
            AddClientPreferences.Down(migrationBuilder);
            AddDraftSubjectAndStagedAttachments.Down(migrationBuilder);
            AddStoredSecrets.Down(migrationBuilder);
            ResolveUserFacingCredentialsToUserRows.Down(migrationBuilder);
            AddUserPasswordCredentials.Down(migrationBuilder);
            AddUserLabelAndDocumentMarker.Down(migrationBuilder);
            KeyMailAccountByUserAndIdentifier.Down(migrationBuilder);
            CarryUserBesideMailAccountReferences.Down(migrationBuilder);
            AddRootSettings.Down(migrationBuilder);
            RetainDatabaseCopyUntilReleased.Down(migrationBuilder);
            AddContentMoveRun.Down(migrationBuilder);
            IndexContentObjectLocators.Down(migrationBuilder);
            AddContactUser.Down(migrationBuilder);
            IndexObjectBackedContentAndRequireItsPayloadEmpty.Down(migrationBuilder);
            BoundEmbeddingSpendAndStoredContentPerUser.Down(migrationBuilder);
            AddJobTurn.Down(migrationBuilder);
            AddContentStorageBackendAndObjectLocator.Down(migrationBuilder);
            AddUserAccounts.Down(migrationBuilder);
            AddMailDraftRecipientProvenance.Down(migrationBuilder);
            AddMailDrafts.Down(migrationBuilder);
            AddOutgoingEmailPrincipal.Down(migrationBuilder);
            AddScheduledAndRecurringSends.Down(migrationBuilder);
            AddOutgoingEmailPeriodUsageIndex.Down(migrationBuilder);
            AddOutgoingEmailFilings.Down(migrationBuilder);
            AddStoredEmailSenderAuthenticationSource.Down(migrationBuilder);
            AddOutgoingRecipientContact.Down(migrationBuilder);
            AddMailRederivationRuns.Down(migrationBuilder);
            AddOutgoingEmailLease.Down(migrationBuilder);
            AddJobEnqueuedTraceContext.Down(migrationBuilder);
            AddStoredEmailMachineAuthorship.Down(migrationBuilder);
            AddMailboxMutationFlagAndKeywordParameters.Down(migrationBuilder);
            AddEmailThreadSurvivorForeignKey.Down(migrationBuilder);
            AddEmailThreadConcurrencyToken.Down(migrationBuilder);
            AddEmailThreads.Down(migrationBuilder);
            AddOutgoingEmailRecord.Down(migrationBuilder);
            AddContacts.Down(migrationBuilder);
            AddMailRederivationPositions.Down(migrationBuilder);
            AddStoredEmailDisplayedAuthorDomain.Down(migrationBuilder);
            ReplaceSenderDomainAlignmentWithAuthorAuthentication.Down(migrationBuilder);
            AddStoredEmailSenderTrust.Down(migrationBuilder);
            AddStoredEmailSenderAuthentication.Down(migrationBuilder);
            AddStoredEmailRemoteKeywords.Down(migrationBuilder);
            AddMailRuleScheduleTrigger.Down(migrationBuilder);
            AddJobDeadLetterIndex.Down(migrationBuilder);
            AddJobFailureRecord.Down(migrationBuilder);
            AddStoredEmailStoredAt.Down(migrationBuilder);
            RestrictJobClaimIndexToClaimableStates.Down(migrationBuilder);
            AddSensitiveContentDerivationStamp.Down(migrationBuilder);
            AddJobs.Down(migrationBuilder);
            AddSpamClassificationRuns.Down(migrationBuilder);
            RenameRuleExecutionDestinationColumn.Down(migrationBuilder);
            AddEmailSpamClassifications.Down(migrationBuilder);
            AddMailRuleExecutionHistory.Down(migrationBuilder);
            AddMailRuleEvaluationState.Down(migrationBuilder);
            AddMailAnsweringAuditEntries.Down(migrationBuilder);
            AddEmbeddingSpendPeriodsAndChunkedTextTruncation.Down(migrationBuilder);
            AddStoredEmailAwaitingContentIndex.Down(migrationBuilder);
            AddEmbeddingProfileLifecycleUniqueIndex.Down(migrationBuilder);
            AddMailboxMutationAuditEntries.Down(migrationBuilder);
            AddAuthoredDeleteLocalDisposition.Down(migrationBuilder);
            AddMailboxMutationObservations.Down(migrationBuilder);
            AddMailboxMutationSourceRemoval.Down(migrationBuilder);
            AddMailboxMutations.Down(migrationBuilder);
            AddEmbeddingProfilesAndEmailEmbeddings.Down(migrationBuilder);
            AddEmailChunks.Down(migrationBuilder);
            AddSynchronizationCheckpointModSeq.Down(migrationBuilder);
            AddMailboxRefreshTokens.Down(migrationBuilder);
            Initial.Down(migrationBuilder);
        }

        /// <summary>The <c>Initial</c> migration, as it was added.</summary>
        private static class Initial
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterDatabase()
                    .Annotation("Npgsql:PostgresExtension:vector", ",,");

                migrationBuilder.CreateTable(
                    name: "backfill_positions",
                    columns: table => new
                    {
                        Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        LastProcessedStoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_backfill_positions", x => x.Name);
                    });

                migrationBuilder.CreateTable(
                    name: "mailbox_accounts",
                    columns: table => new
                    {
                        Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mailbox_accounts", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "mail_folders",
                    columns: table => new
                    {
                        Id = table.Column<long>(type: "bigint", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        ResolutionGeneration = table.Column<int>(type: "integer", nullable: false),
                        RemotePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                        HierarchyDelimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_folders", x => x.Id);
                        table.ForeignKey(
                            name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                            column: x => x.MailboxAccountId,
                            principalTable: "mailbox_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "stored_emails",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                        UidValidity = table.Column<long>(type: "bigint", nullable: false),
                        Uid = table.Column<long>(type: "bigint", nullable: false),
                        InternetMessageId = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                        Subject = table.Column<string>(type: "text", nullable: true),
                        SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        SizeOctets = table.Column<long>(type: "bigint", nullable: false),
                        ContentAvailability = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        SenderDisplayName = table.Column<string>(type: "text", nullable: true),
                        SenderAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                        SenderNormalizedAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                        ToAddresses = table.Column<string[]>(type: "text[]", nullable: false),
                        CcAddresses = table.Column<string[]>(type: "text[]", nullable: false),
                        ReplyToAddresses = table.Column<string[]>(type: "text[]", nullable: false),
                        InReplyTo = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                        ThreadReferences = table.Column<string[]>(type: "text[]", nullable: false),
                        AttachmentCount = table.Column<int>(type: "integer", nullable: false),
                        AttachmentTotalSizeOctets = table.Column<long>(type: "bigint", nullable: false),
                        InlineResourceCount = table.Column<int>(type: "integer", nullable: false),
                        IsEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                        CarriesUnverifiedSignature = table.Column<bool>(type: "boolean", nullable: false),
                        ContainsUnexpandedTnefPart = table.Column<bool>(type: "boolean", nullable: false),
                        RemoteFlagsObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        RemoteExpungeObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        IsRemotelySeen = table.Column<bool>(type: "boolean", nullable: false),
                        IsRemotelyAnswered = table.Column<bool>(type: "boolean", nullable: false),
                        IsRemotelyFlagged = table.Column<bool>(type: "boolean", nullable: false),
                        IsRemotelyDraft = table.Column<bool>(type: "boolean", nullable: false),
                        IsRemotelyDeleted = table.Column<bool>(type: "boolean", nullable: false),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_stored_emails", x => x.Id);
                        table.ForeignKey(
                            name: "FK_stored_emails_mail_folders_MailFolderId",
                            column: x => x.MailFolderId,
                            principalTable: "mail_folders",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "synchronization_checkpoints",
                    columns: table => new
                    {
                        MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                        UidValidity = table.Column<long>(type: "bigint", nullable: false),
                        LastSeenUid = table.Column<long>(type: "bigint", nullable: true),
                        SynchronizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_synchronization_checkpoints", x => x.MailFolderId);
                        table.ForeignKey(
                            name: "FK_synchronization_checkpoints_mail_folders_MailFolderId",
                            column: x => x.MailFolderId,
                            principalTable: "mail_folders",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "email_content_repair_requests",
                    columns: table => new
                    {
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        Defect = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        FirstRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        LastRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        RequestCount = table.Column<int>(type: "integer", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_email_content_repair_requests", x => x.StoredEmailId);
                        table.ForeignKey(
                            name: "FK_email_content_repair_requests_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "email_message_contents",
                    columns: table => new
                    {
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        RawMime = table.Column<byte[]>(type: "bytea", nullable: false),
                        MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                        Sha256Hash = table.Column<byte[]>(type: "bytea", nullable: false),
                        StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_email_message_contents", x => x.StoredEmailId);
                        table.ForeignKey(
                            name: "FK_email_message_contents_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "email_search_documents",
                    columns: table => new
                    {
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        SubjectText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                        ParticipantAddresses = table.Column<string>(type: "text", nullable: true),
                        BodyText = table.Column<string>(type: "text", nullable: true),
                        BodyTextBeforeTrimming = table.Column<string>(type: "text", nullable: true),
                        TextSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        ExtractedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false)
                            .Annotation("Npgsql:TsVectorConfig", "simple")
                            .Annotation("Npgsql:TsVectorProperties", new[] { "SubjectText", "ParticipantAddresses", "BodyText" })
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_email_search_documents", x => x.StoredEmailId);
                        table.ForeignKey(
                            name: "FK_email_search_documents_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_email_search_documents_search_vector",
                    table: "email_search_documents",
                    column: "SearchVector")
                    .Annotation("Npgsql:IndexMethod", "GIN");

                migrationBuilder.CreateIndex(
                    name: "ix_mail_folders_account_alias_generation",
                    table: "mail_folders",
                    columns: new[] { "MailboxAccountId", "Alias", "ResolutionGeneration" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_account_timeline",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "ReceivedAt", "Id" },
                    descending: new[] { false, true, true })
                    .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_cc_addresses",
                    table: "stored_emails",
                    column: "CcAddresses")
                    .Annotation("Npgsql:IndexMethod", "GIN");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_folder_timeline",
                    table: "stored_emails",
                    columns: new[] { "MailFolderId", "ReceivedAt", "Id" },
                    descending: new[] { false, true, true })
                    .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_folder_uidvalidity_uid",
                    table: "stored_emails",
                    columns: new[] { "MailFolderId", "UidValidity", "Uid" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_reconciliation_queue",
                    table: "stored_emails",
                    columns: new[] { "MailFolderId", "RemoteFlagsObservedAt", "Uid" },
                    filter: "\"RemoteExpungeObservedAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_reply_to_addresses",
                    table: "stored_emails",
                    column: "ReplyToAddresses")
                    .Annotation("Npgsql:IndexMethod", "GIN");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_sender",
                    table: "stored_emails",
                    column: "SenderNormalizedAddress");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_to_addresses",
                    table: "stored_emails",
                    column: "ToAddresses")
                    .Annotation("Npgsql:IndexMethod", "GIN");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "backfill_positions");

                migrationBuilder.DropTable(
                    name: "email_content_repair_requests");

                migrationBuilder.DropTable(
                    name: "email_message_contents");

                migrationBuilder.DropTable(
                    name: "email_search_documents");

                migrationBuilder.DropTable(
                    name: "synchronization_checkpoints");

                migrationBuilder.DropTable(
                    name: "stored_emails");

                migrationBuilder.DropTable(
                    name: "mail_folders");

                migrationBuilder.DropTable(
                    name: "mailbox_accounts");
            }
        }

        /// <summary>The <c>AddMailboxRefreshTokens</c> migration, as it was added.</summary>
        private static class AddMailboxRefreshTokens
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "mailbox_refresh_tokens",
                    columns: table => new
                    {
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        SealedRefreshToken = table.Column<byte[]>(type: "bytea", nullable: false),
                        DataEncryptionKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mailbox_refresh_tokens", x => x.MailboxAccountId);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_refresh_tokens_data_encryption_key",
                    table: "mailbox_refresh_tokens",
                    column: "DataEncryptionKeyId");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mailbox_refresh_tokens");
            }
        }

        /// <summary>The <c>AddSynchronizationCheckpointModSeq</c> migration, as it was added.</summary>
        private static class AddSynchronizationCheckpointModSeq
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<long>(
                    name: "ReconciledThroughModSeq",
                    table: "synchronization_checkpoints",
                    type: "bigint",
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "ReconciledThroughModSeq",
                    table: "synchronization_checkpoints");
            }
        }

        /// <summary>The <c>AddEmailChunks</c> migration, as it was added.</summary>
        private static class AddEmailChunks
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "email_chunks",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        Ordinal = table.Column<int>(type: "integer", nullable: false),
                        StartOffset = table.Column<int>(type: "integer", nullable: false),
                        Text = table.Column<string>(type: "text", nullable: false),
                        ContentHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                        RuleSetVersion = table.Column<int>(type: "integer", nullable: false),
                        IsDerivedFromLossyHtml = table.Column<bool>(type: "boolean", nullable: false),
                        DerivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_email_chunks", x => x.Id);
                        table.ForeignKey(
                            name: "FK_email_chunks_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_email_chunks_email_ordinal",
                    table: "email_chunks",
                    columns: new[] { "StoredEmailId", "Ordinal" },
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "email_chunks");
            }
        }

        /// <summary>The <c>AddEmbeddingProfilesAndEmailEmbeddings</c> migration, as it was added.</summary>
        private static class AddEmbeddingProfilesAndEmailEmbeddings
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "embedding_profiles",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        ModelIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        Dimension = table.Column<int>(type: "integer", nullable: false),
                        DistanceMetric = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        InputCharacterLimit = table.Column<int>(type: "integer", nullable: false),
                        PassageInstruction = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        NormalizesVector = table.Column<bool>(type: "boolean", nullable: false),
                        IdentityFingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                        LifecycleState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_embedding_profiles", x => x.Id);
                        table.UniqueConstraint("ak_embedding_profiles_id_dimension", x => new { x.Id, x.Dimension });
                    });

                migrationBuilder.CreateTable(
                    name: "email_embeddings",
                    columns: table => new
                    {
                        EmailChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                        EmbeddingProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                        Dimension = table.Column<int>(type: "integer", nullable: false),
                        Embedding = table.Column<Vector>(type: "vector", nullable: false),
                        GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_email_embeddings", x => new { x.EmailChunkId, x.EmbeddingProfileId });
                        table.CheckConstraint("ck_email_embeddings_dimension", "vector_dims(\"Embedding\") = \"Dimension\"");
                        table.ForeignKey(
                            name: "FK_email_embeddings_email_chunks_EmailChunkId",
                            column: x => x.EmailChunkId,
                            principalTable: "email_chunks",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "fk_email_embeddings_embedding_profiles",
                            columns: x => new { x.EmbeddingProfileId, x.Dimension },
                            principalTable: "embedding_profiles",
                            principalColumns: new[] { "Id", "Dimension" },
                            onDelete: ReferentialAction.Restrict);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_email_embeddings_profile",
                    table: "email_embeddings",
                    columns: new[] { "EmbeddingProfileId", "Dimension" });

                migrationBuilder.CreateIndex(
                    name: "ix_embedding_profiles_identity_fingerprint",
                    table: "embedding_profiles",
                    column: "IdentityFingerprint",
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "email_embeddings");

                migrationBuilder.DropTable(
                    name: "embedding_profiles");
            }
        }

        /// <summary>The <c>AddMailboxMutations</c> migration, as it was added.</summary>
        private static class AddMailboxMutations
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "mailbox_mutations",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                        UidValidity = table.Column<long>(type: "bigint", nullable: false),
                        Uid = table.Column<long>(type: "bigint", nullable: false),
                        Mutation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        DestinationFolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        DestinationHierarchyDelimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                        DesiredSeenState = table.Column<bool>(type: "boolean", nullable: true),
                        Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        PlacementUidValidity = table.Column<long>(type: "bigint", nullable: true),
                        PlacementUid = table.Column<long>(type: "bigint", nullable: true),
                        AttemptCount = table.Column<int>(type: "integer", nullable: false),
                        RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        StageChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        LastFailureCode = table.Column<int>(type: "integer", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mailbox_mutations", x => x.Id);
                        table.ForeignKey(
                            name: "FK_mailbox_mutations_mail_folders_MailFolderId",
                            column: x => x.MailFolderId,
                            principalTable: "mail_folders",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "FK_mailbox_mutations_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_identity",
                    table: "mailbox_mutations",
                    columns: new[] { "MailFolderId", "UidValidity", "Uid", "RequesterOrigin", "RequesterIdentity", "Mutation" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_outstanding",
                    table: "mailbox_mutations",
                    columns: new[] { "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" <> 'Completed'");

                migrationBuilder.CreateIndex(
                    name: "IX_mailbox_mutations_StoredEmailId",
                    table: "mailbox_mutations",
                    column: "StoredEmailId");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mailbox_mutations");
            }
        }

        /// <summary>The <c>AddMailboxMutationSourceRemoval</c> migration, as it was added.</summary>
        private static class AddMailboxMutationSourceRemoval
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<bool>(
                    name: "RequiresSourceRemoval",
                    table: "mailbox_mutations",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "RequiresSourceRemoval",
                    table: "mailbox_mutations");
            }
        }

        /// <summary>The <c>AddMailboxMutationObservations</c> migration, as it was added.</summary>
        private static class AddMailboxMutationObservations
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "PlacementObservedAt",
                    table: "mailbox_mutations",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "SourceRemovalObservedAt",
                    table: "mailbox_mutations",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations",
                    columns: new[] { "MailboxAccountId", "DestinationFolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"PlacementObservedAt\" IS NULL");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations");

                migrationBuilder.DropColumn(
                    name: "PlacementObservedAt",
                    table: "mailbox_mutations");

                migrationBuilder.DropColumn(
                    name: "SourceRemovalObservedAt",
                    table: "mailbox_mutations");
            }
        }

        /// <summary>The <c>AddAuthoredDeleteLocalDisposition</c> migration, as it was added.</summary>
        private static class AddAuthoredDeleteLocalDisposition
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<bool>(
                    name: "IsRetainedAfterAuthoredDelete",
                    table: "stored_emails",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);

                migrationBuilder.AddColumn<string>(
                    name: "LocalDisposition",
                    table: "mailbox_mutations",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "IsRetainedAfterAuthoredDelete",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "LocalDisposition",
                    table: "mailbox_mutations");
            }
        }

        /// <summary>The <c>AddMailboxMutationAuditEntries</c> migration, as it was added.</summary>
        private static class AddMailboxMutationAuditEntries
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<bool>(
                    name: "AuditTrailEnabled",
                    table: "mailbox_mutations",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);

                migrationBuilder.CreateTable(
                    name: "mailbox_mutation_audit_entries",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MutationRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        Mutation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        SourceFolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                        SourceHierarchyDelimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                        SourceUidValidity = table.Column<long>(type: "bigint", nullable: false),
                        SourceUid = table.Column<long>(type: "bigint", nullable: false),
                        DestinationFolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        DestinationHierarchyDelimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                        PlacementUidValidity = table.Column<long>(type: "bigint", nullable: true),
                        PlacementUid = table.Column<long>(type: "bigint", nullable: true),
                        DesiredSeenState = table.Column<bool>(type: "boolean", nullable: true),
                        RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        FailureCode = table.Column<int>(type: "integer", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mailbox_mutation_audit_entries", x => x.Id);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutation_audit_entries_account_completed",
                    table: "mailbox_mutation_audit_entries",
                    columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutation_audit_entries_mutation",
                    table: "mailbox_mutation_audit_entries",
                    column: "MutationRecordId",
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mailbox_mutation_audit_entries");

                migrationBuilder.DropColumn(
                    name: "AuditTrailEnabled",
                    table: "mailbox_mutations");
            }
        }

        /// <summary>The <c>AddEmbeddingProfileLifecycleUniqueIndex</c> migration, as it was added.</summary>
        private static class AddEmbeddingProfileLifecycleUniqueIndex
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateIndex(
                    name: "ix_embedding_profiles_lifecycle_state",
                    table: "embedding_profiles",
                    column: "LifecycleState",
                    unique: true,
                    filter: "\"LifecycleState\" IN ('Building', 'Active')");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_embedding_profiles_lifecycle_state",
                    table: "embedding_profiles");
            }
        }

        /// <summary>The <c>AddStoredEmailAwaitingContentIndex</c> migration, as it was added.</summary>
        private static class AddStoredEmailAwaitingContentIndex
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_content",
                    table: "stored_emails",
                    columns: new[] { "MailFolderId", "UidValidity", "Uid" },
                    filter: "\"ContentAvailability\" = 'AwaitingStorageHeadroom'");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_content",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddEmbeddingSpendPeriodsAndChunkedTextTruncation</c> migration, as it was added.</summary>
        private static class AddEmbeddingSpendPeriodsAndChunkedTextTruncation
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<int>(
                    name: "ChunkedTextTruncatedFromCharacterCount",
                    table: "stored_emails",
                    type: "integer",
                    nullable: true);

                migrationBuilder.CreateTable(
                    name: "embedding_spend_periods",
                    columns: table => new
                    {
                        PeriodStartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        ConsumedInputCharacterCount = table.Column<long>(type: "bigint", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_embedding_spend_periods", x => x.PeriodStartsAt);
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "embedding_spend_periods");

                migrationBuilder.DropColumn(
                    name: "ChunkedTextTruncatedFromCharacterCount",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddMailAnsweringAuditEntries</c> migration, as it was added.</summary>
        private static class AddMailAnsweringAuditEntries
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "mail_answering_audit_entries",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        RunId = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        ChatEndpointAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        InstructionsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Degradation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_answering_audit_entries", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "mail_answering_audited_emails",
                    columns: table => new
                    {
                        MailAnsweringAuditEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        Position = table.Column<int>(type: "integer", nullable: false),
                        WasCited = table.Column<bool>(type: "boolean", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_answering_audited_emails", x => new { x.MailAnsweringAuditEntryId, x.StoredEmailId });
                        table.ForeignKey(
                            name: "FK_mail_answering_audited_emails_mail_answering_audit_entries_~",
                            column: x => x.MailAnsweringAuditEntryId,
                            principalTable: "mail_answering_audit_entries",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "FK_mail_answering_audited_emails_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_account_completed",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_run_account",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "RunId", "MailboxAccountId" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "IX_mail_answering_audited_emails_StoredEmailId",
                    table: "mail_answering_audited_emails",
                    column: "StoredEmailId");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mail_answering_audited_emails");

                migrationBuilder.DropTable(
                    name: "mail_answering_audit_entries");
            }
        }

        /// <summary>The <c>AddMailRuleEvaluationState</c> migration, as it was added.</summary>
        private static class AddMailRuleEvaluationState
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "RulesEvaluatedAt",
                    table: "stored_emails",
                    type: "timestamp with time zone",
                    nullable: true);

                // Every message already stored is stamped as evaluated, and the stamp is the point of the column rather
                // than a tidying of it. The arrival queue is "rows carrying no value", so without this an upgrade would
                // hand the deployment's first rule set an entire mailbox's history as though it had all just arrived —
                // which, once a match leads to a change to the mail, is a mass filing nobody asked for. Running the rules
                // over mail already stored stays available as the whole-mailbox run an owner requests deliberately.
                // One statement over the table at upgrade time, before the partial index below is built over the result.
                migrationBuilder.Sql("UPDATE stored_emails SET \"RulesEvaluatedAt\" = now() WHERE \"RulesEvaluatedAt\" IS NULL;");

                migrationBuilder.CreateTable(
                    name: "mail_rule_evaluation_runs",
                    columns: table => new
                    {
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        Revision = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: true),
                        Position = table.Column<Guid>(type: "uuid", nullable: true),
                        EvaluatedEmailCount = table.Column<int>(type: "integer", nullable: false),
                        MatchedEmailCount = table.Column<int>(type: "integer", nullable: false),
                        SkippedEmailCount = table.Column<int>(type: "integer", nullable: false),
                        EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        Ending = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_mail_rule_evaluation_runs", x => x.MailboxAccountId);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_account_identity",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "Id" },
                    filter: "\"RulesEvaluatedAt\" IS NULL");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mail_rule_evaluation_runs");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_account_identity",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "RulesEvaluatedAt",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddMailRuleExecutionHistory</c> migration, as it was added.</summary>
        private static class AddMailRuleExecutionHistory
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "mail_rule_executions",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        RuleName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Revision = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: false),
                        Trigger = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        ConditionFailure = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        ReadFacts = table.Column<string[]>(type: "text[]", nullable: false),
                        EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        Duration = table.Column<TimeSpan>(type: "interval", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_rule_executions", x => x.Id);
                        table.ForeignKey(
                            name: "FK_mail_rule_executions_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "mail_rule_executed_actions",
                    columns: table => new
                    {
                        MailRuleExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                        Position = table.Column<int>(type: "integer", nullable: false),
                        Mutation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        DestinationAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        FailureReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        MutationRecordId = table.Column<Guid>(type: "uuid", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_rule_executed_actions", x => new { x.MailRuleExecutionId, x.Position });
                        table.ForeignKey(
                            name: "FK_mail_rule_executed_actions_mail_rule_executions_MailRuleExe~",
                            column: x => x.MailRuleExecutionId,
                            principalTable: "mail_rule_executions",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_account_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "MailboxAccountId", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_account_rule_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "MailboxAccountId", "RuleName", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_email_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "StoredEmailId", "EvaluatedAt", "Id" });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mail_rule_executed_actions");

                migrationBuilder.DropTable(
                    name: "mail_rule_executions");
            }
        }

        /// <summary>The <c>AddEmailSpamClassifications</c> migration, as it was added.</summary>
        private static class AddEmailSpamClassifications
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "email_spam_classifications",
                    columns: table => new
                    {
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        Verdict = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        DecidedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Score = table.Column<double>(type: "double precision", nullable: true),
                        Threshold = table.Column<double>(type: "double precision", nullable: true),
                        CorpusRevision = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_email_spam_classifications", x => x.StoredEmailId);
                        table.ForeignKey(
                            name: "FK_email_spam_classifications_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "email_spam_classification_signals",
                    columns: table => new
                    {
                        Id = table.Column<long>(type: "bigint", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        Ordinal = table.Column<int>(type: "integer", nullable: false),
                        Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Observation = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Origin = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_email_spam_classification_signals", x => x.Id);
                        table.ForeignKey(
                            name: "fk_email_spam_classification_signals_classifications",
                            column: x => x.StoredEmailId,
                            principalTable: "email_spam_classifications",
                            principalColumn: "StoredEmailId",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_email_spam_classification_signals_classification_ordinal",
                    table: "email_spam_classification_signals",
                    columns: new[] { "StoredEmailId", "Ordinal" },
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "email_spam_classification_signals");

                migrationBuilder.DropTable(
                    name: "email_spam_classifications");
            }
        }

        /// <summary>The <c>RenameRuleExecutionDestinationColumn</c> migration, as it was added.</summary>
        private static class RenameRuleExecutionDestinationColumn
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.RenameColumn(
                    name: "DestinationAlias",
                    table: "mail_rule_executed_actions",
                    newName: "Destination");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.RenameColumn(
                    name: "Destination",
                    table: "mail_rule_executed_actions",
                    newName: "DestinationAlias");
            }
        }

        /// <summary>The <c>AddSpamClassificationRuns</c> migration, as it was added.</summary>
        private static class AddSpamClassificationRuns
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Profile",
                    table: "email_spam_classifications",
                    type: "character(12)",
                    fixedLength: true,
                    maxLength: 12,
                    nullable: true);

                migrationBuilder.CreateTable(
                    name: "spam_classification_runs",
                    columns: table => new
                    {
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        FolderAliases = table.Column<string[]>(type: "text[]", nullable: false),
                        Posture = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Rescores = table.Column<bool>(type: "boolean", nullable: false),
                        Profile = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: true),
                        Position = table.Column<Guid>(type: "uuid", nullable: true),
                        ClassifiedEmailCount = table.Column<int>(type: "integer", nullable: false),
                        SpamEmailCount = table.Column<int>(type: "integer", nullable: false),
                        UndeterminedEmailCount = table.Column<int>(type: "integer", nullable: false),
                        SkippedEmailCount = table.Column<int>(type: "integer", nullable: false),
                        UnclassifiableEmailCount = table.Column<int>(type: "integer", nullable: false),
                        ActedEmailCount = table.Column<int>(type: "integer", nullable: false),
                        EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        Ending = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_spam_classification_runs", x => x.MailboxAccountId);
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "spam_classification_runs");

                migrationBuilder.DropColumn(
                    name: "Profile",
                    table: "email_spam_classifications");
            }
        }

        /// <summary>The <c>AddJobs</c> migration, as it was added.</summary>
        private static class AddJobs
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "jobs",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        JobType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                        Payload = table.Column<string>(type: "jsonb", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        EnqueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        StateChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        AttemptCount = table.Column<int>(type: "integer", nullable: false),
                        LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_jobs", x => x.Id);
                        table.ForeignKey(
                            name: "FK_jobs_mailbox_accounts_MailboxAccountId",
                            column: x => x.MailboxAccountId,
                            principalTable: "mailbox_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_account",
                    table: "jobs",
                    columns: new[] { "MailboxAccountId", "EnqueuedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_claimable",
                    table: "jobs",
                    columns: new[] { "JobType", "AvailableAt" },
                    filter: "\"State\" <> 'Succeeded'");

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_identity",
                    table: "jobs",
                    columns: new[] { "JobType", "IdempotencyKey" },
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "jobs");
            }
        }

        /// <summary>The <c>AddSensitiveContentDerivationStamp</c> migration, as it was added.</summary>
        private static class AddSensitiveContentDerivationStamp
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "SensitiveContentStamp",
                    table: "email_search_documents",
                    type: "character(64)",
                    fixedLength: true,
                    maxLength: 64,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "SensitiveContentStamp",
                    table: "backfill_positions",
                    type: "character(64)",
                    fixedLength: true,
                    maxLength: 64,
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "SensitiveContentStamp",
                    table: "email_search_documents");

                migrationBuilder.DropColumn(
                    name: "SensitiveContentStamp",
                    table: "backfill_positions");
            }
        }

        /// <summary>The <c>RestrictJobClaimIndexToClaimableStates</c> migration, as it was added.</summary>
        private static class RestrictJobClaimIndexToClaimableStates
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_jobs_claimable",
                    table: "jobs");

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_claimable",
                    table: "jobs",
                    columns: new[] { "JobType", "AvailableAt" },
                    filter: "\"State\" IN ('Pending', 'Claimed')");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_jobs_claimable",
                    table: "jobs");

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_claimable",
                    table: "jobs",
                    columns: new[] { "JobType", "AvailableAt" },
                    filter: "\"State\" <> 'Succeeded'");
            }
        }

        /// <summary>The <c>AddStoredEmailStoredAt</c> migration, as it was added.</summary>
        private static class AddStoredEmailStoredAt
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                // Npgsql writes the minimum value as `-infinity`, which is the backfill this column wants rather than an
                // artefact of it: a row stored before this migration has by definition waited longer than any classification
                // wait a deployment can configure, so it is eligible immediately. Stamping the upgrade instant instead would
                // hold a whole mailbox out of the index for one more wait apiece.
                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "StoredAt",
                    table: "stored_emails",
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "StoredAt",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddJobFailureRecord</c> migration, as it was added.</summary>
        private static class AddJobFailureRecord
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "LastFailureClassification",
                    table: "jobs",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "LastFailureReason",
                    table: "jobs",
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: true);

                // The terminal failure state is now named for what it is, and the column holds a state's name rather than
                // an ordinal, so a row written before this migration still reads 'Failed'. Nothing else would correct it,
                // and such a row would sit outside every query about dead letters — which is the one job state an operator
                // is meant to act on.
                migrationBuilder.Sql("""UPDATE jobs SET "State" = 'DeadLettered' WHERE "State" = 'Failed';""");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.Sql("""UPDATE jobs SET "State" = 'Failed' WHERE "State" = 'DeadLettered';""");

                migrationBuilder.DropColumn(
                    name: "LastFailureClassification",
                    table: "jobs");

                migrationBuilder.DropColumn(
                    name: "LastFailureReason",
                    table: "jobs");
            }
        }

        /// <summary>The <c>AddJobDeadLetterIndex</c> migration, as it was added.</summary>
        private static class AddJobDeadLetterIndex
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateIndex(
                    name: "ix_jobs_dead_lettered",
                    table: "jobs",
                    columns: new[] { "StateChangedAt", "Id" },
                    filter: "\"State\" = 'DeadLettered'");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_jobs_dead_lettered",
                    table: "jobs");
            }
        }

        /// <summary>The <c>AddMailRuleScheduleTrigger</c> migration, as it was added.</summary>
        private static class AddMailRuleScheduleTrigger
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Trigger",
                    table: "mail_rule_evaluation_runs",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "RequestedRun");

                migrationBuilder.CreateTable(
                    name: "job_schedules",
                    columns: table => new
                    {
                        ScheduleId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                        ObservedFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        LastOccurrenceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        LastDispatchedJobId = table.Column<Guid>(type: "uuid", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_job_schedules", x => x.ScheduleId);
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "job_schedules");

                migrationBuilder.DropColumn(
                    name: "Trigger",
                    table: "mail_rule_evaluation_runs");
            }
        }

        // Authored against a base that did not yet carry 20260815201928_AddStoredEmailSenderAuthentication, so this
        // identifier sorts before a migration that merged first and is never the newest one a filename sort finds. It is
        // permanent, because a database has already written it into __EFMigrationsHistory, and the ordering costs nothing
        // at runtime: both migrations only add columns and indexes to stored_emails, and either order leaves one schema.
        // What it does cost is scripts/script-migration.sh with no arguments, which reads that sort and would script the
        // other migration. Review this one's SQL by naming the pair:
        //   scripts/script-migration.sh 20260813161625_AddMailRuleScheduleTrigger 20260815165050_AddStoredEmailRemoteKeywords

        /// <summary>The <c>AddStoredEmailRemoteKeywords</c> migration, as it was added.</summary>
        private static class AddStoredEmailRemoteKeywords
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string[]>(
                    name: "RemoteKeywords",
                    table: "stored_emails",
                    type: "text[]",
                    nullable: false,
                    defaultValue: new string[0]);

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_remote_keywords",
                    table: "stored_emails",
                    column: "RemoteKeywords")
                    .Annotation("Npgsql:IndexMethod", "GIN");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_remote_keywords",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "RemoteKeywords",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddStoredEmailSenderAuthentication</c> migration, as it was added.</summary>
        private static class AddStoredEmailSenderAuthentication
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "AuthenticatedSenderDomain",
                    table: "stored_emails",
                    type: "character varying(253)",
                    maxLength: 253,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "DkimSignerDomain",
                    table: "stored_emails",
                    type: "character varying(253)",
                    maxLength: 253,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "DmarcOutcome",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "NotReported");

                migrationBuilder.AddColumn<string>(
                    name: "SenderAuthenticationMethod",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "None");

                migrationBuilder.AddColumn<string>(
                    name: "SenderAuthenticationOutcome",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "NotEstablished");

                migrationBuilder.AddColumn<string>(
                    name: "SenderDomainAlignment",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "NotAssessed");

                migrationBuilder.AddColumn<string>(
                    name: "SpfMailFromDomain",
                    table: "stored_emails",
                    type: "character varying(253)",
                    maxLength: 253,
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "AuthenticatedSenderDomain",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "DkimSignerDomain",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "DmarcOutcome",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "SenderAuthenticationMethod",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "SenderAuthenticationOutcome",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "SenderDomainAlignment",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "SpfMailFromDomain",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddStoredEmailSenderTrust</c> migration, as it was added.</summary>
        private static class AddStoredEmailSenderTrust
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "SenderTrustGrantedBy",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "None");

                migrationBuilder.AddColumn<string>(
                    name: "SenderTrustLevel",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "Unknown");

                migrationBuilder.AddColumn<string>(
                    name: "SenderTrustPolicyRevision",
                    table: "stored_emails",
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "SenderTrustGrantedBy",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "SenderTrustLevel",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "SenderTrustPolicyRevision",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>ReplaceSenderDomainAlignmentWithAuthorAuthentication</c> migration, as it was added.</summary>
        private static class ReplaceSenderDomainAlignmentWithAuthorAuthentication
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "SenderDomainAlignment",
                    table: "stored_emails");

                migrationBuilder.AddColumn<string>(
                    name: "AuthenticatedAuthorDomain",
                    table: "stored_emails",
                    type: "character varying(253)",
                    maxLength: 253,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "AuthorAuthenticationOutcome",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "NotEstablished");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "AuthenticatedAuthorDomain",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "AuthorAuthenticationOutcome",
                    table: "stored_emails");

                migrationBuilder.AddColumn<string>(
                    name: "SenderDomainAlignment",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "NotAssessed");
            }
        }

        /// <summary>The <c>AddStoredEmailDisplayedAuthorDomain</c> migration, as it was added.</summary>
        private static class AddStoredEmailDisplayedAuthorDomain
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "DisplayedAuthorDomain",
                    table: "stored_emails",
                    type: "character varying(253)",
                    maxLength: 253,
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "DisplayedAuthorDomain",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddMailRederivationPositions</c> migration, as it was added.</summary>
        private static class AddMailRederivationPositions
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "mail_rederivation_positions",
                    columns: table => new
                    {
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        FolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        LastProcessedStoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_mail_rederivation_positions", x => new { x.MailboxAccountId, x.FolderAlias });
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mail_rederivation_positions");
            }
        }

        /// <summary>The <c>AddContacts</c> migration, as it was added.</summary>
        private static class AddContacts
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "contacts",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                        DisplayNameSortKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                        PreferredNormalizedAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                        Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                        Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        AmendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_contacts", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "contact_addresses",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                        Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                        NormalizedAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_contact_addresses", x => x.Id);
                        table.ForeignKey(
                            name: "FK_contact_addresses_contacts_ContactId",
                            column: x => x.ContactId,
                            principalTable: "contacts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_contact_addresses_ContactId",
                    table: "contact_addresses",
                    column: "ContactId");

                migrationBuilder.CreateIndex(
                    name: "ix_contact_addresses_normalized_address",
                    table: "contact_addresses",
                    column: "NormalizedAddress",
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_contacts_display_name_sort_key_id",
                    table: "contacts",
                    columns: new[] { "DisplayNameSortKey", "Id" });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "contact_addresses");

                migrationBuilder.DropTable(
                    name: "contacts");
            }
        }

        /// <summary>The <c>AddOutgoingEmailRecord</c> migration, as it was added.</summary>
        private static class AddOutgoingEmailRecord
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "outgoing_emails",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                        AttemptCount = table.Column<int>(type: "integer", nullable: false),
                        RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        StageChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        LastFailureCode = table.Column<int>(type: "integer", nullable: true),
                        LastReplyCode = table.Column<int>(type: "integer", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_outgoing_emails", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "outgoing_email_contents",
                    columns: table => new
                    {
                        OutgoingEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        RawMime = table.Column<byte[]>(type: "bytea", nullable: false),
                        MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                        Sha256Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                        StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_outgoing_email_contents", x => x.OutgoingEmailId);
                        table.ForeignKey(
                            name: "fk_outgoing_email_contents_emails",
                            column: x => x.OutgoingEmailId,
                            principalTable: "outgoing_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "outgoing_email_recipients",
                    columns: table => new
                    {
                        OutgoingEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        Ordinal = table.Column<int>(type: "integer", nullable: false),
                        Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                        Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        LastReplyCode = table.Column<int>(type: "integer", nullable: true),
                        AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_outgoing_email_recipients", x => new { x.OutgoingEmailId, x.Ordinal });
                        table.ForeignKey(
                            name: "fk_outgoing_email_recipients_emails",
                            column: x => x.OutgoingEmailId,
                            principalTable: "outgoing_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_identity",
                    table: "outgoing_emails",
                    columns: new[] { "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_outstanding",
                    table: "outgoing_emails",
                    columns: new[] { "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" NOT IN ('Sent', 'Refused', 'Cancelled')");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "outgoing_email_contents");

                migrationBuilder.DropTable(
                    name: "outgoing_email_recipients");

                migrationBuilder.DropTable(
                    name: "outgoing_emails");
            }
        }

        /// <summary>The <c>AddEmailThreads</c> migration, as it was added.</summary>
        private static class AddEmailThreads
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "EmailThreadId",
                    table: "stored_emails",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "ParentStoredEmailId",
                    table: "stored_emails",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.CreateTable(
                    name: "email_threads",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        AssembledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        MergedIntoEmailThreadId = table.Column<Guid>(type: "uuid", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_email_threads", x => x.Id);
                        table.ForeignKey(
                            name: "FK_email_threads_mailbox_accounts_MailboxAccountId",
                            column: x => x.MailboxAccountId,
                            principalTable: "mailbox_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "email_thread_identifiers",
                    columns: table => new
                    {
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        IdentifierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        EmailThreadId = table.Column<Guid>(type: "uuid", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_email_thread_identifiers", x => new { x.MailboxAccountId, x.IdentifierHash });
                        table.ForeignKey(
                            name: "FK_email_thread_identifiers_email_threads_EmailThreadId",
                            column: x => x.EmailThreadId,
                            principalTable: "email_threads",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_stored_emails_ParentStoredEmailId",
                    table: "stored_emails",
                    column: "ParentStoredEmailId");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_thread",
                    table: "stored_emails",
                    columns: new[] { "EmailThreadId", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_email_thread_identifiers_thread",
                    table: "email_thread_identifiers",
                    column: "EmailThreadId");

                migrationBuilder.CreateIndex(
                    name: "IX_email_threads_MailboxAccountId",
                    table: "email_threads",
                    column: "MailboxAccountId");

                migrationBuilder.AddForeignKey(
                    name: "FK_stored_emails_email_threads_EmailThreadId",
                    table: "stored_emails",
                    column: "EmailThreadId",
                    principalTable: "email_threads",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);

                migrationBuilder.AddForeignKey(
                    name: "FK_stored_emails_stored_emails_ParentStoredEmailId",
                    table: "stored_emails",
                    column: "ParentStoredEmailId",
                    principalTable: "stored_emails",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_stored_emails_email_threads_EmailThreadId",
                    table: "stored_emails");

                migrationBuilder.DropForeignKey(
                    name: "FK_stored_emails_stored_emails_ParentStoredEmailId",
                    table: "stored_emails");

                migrationBuilder.DropTable(
                    name: "email_thread_identifiers");

                migrationBuilder.DropTable(
                    name: "email_threads");

                migrationBuilder.DropIndex(
                    name: "IX_stored_emails_ParentStoredEmailId",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_thread",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "EmailThreadId",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "ParentStoredEmailId",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddEmailThreadConcurrencyToken</c> migration, as it was added.</summary>
        private static class AddEmailThreadConcurrencyToken
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<uint>(
                    name: "xmin",
                    table: "email_threads",
                    type: "xid",
                    rowVersion: true,
                    nullable: false,
                    defaultValue: 0u);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "xmin",
                    table: "email_threads");
            }
        }

        /// <summary>The <c>AddEmailThreadSurvivorForeignKey</c> migration, as it was added.</summary>
        private static class AddEmailThreadSurvivorForeignKey
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateIndex(
                    name: "IX_email_threads_MergedIntoEmailThreadId",
                    table: "email_threads",
                    column: "MergedIntoEmailThreadId");

                migrationBuilder.AddForeignKey(
                    name: "FK_email_threads_email_threads_MergedIntoEmailThreadId",
                    table: "email_threads",
                    column: "MergedIntoEmailThreadId",
                    principalTable: "email_threads",
                    principalColumn: "Id");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_email_threads_email_threads_MergedIntoEmailThreadId",
                    table: "email_threads");

                migrationBuilder.DropIndex(
                    name: "IX_email_threads_MergedIntoEmailThreadId",
                    table: "email_threads");
            }
        }

        /// <summary>The <c>AddMailboxMutationFlagAndKeywordParameters</c> migration, as it was added.</summary>
        private static class AddMailboxMutationFlagAndKeywordParameters
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<bool>(
                    name: "DesiredFlaggedState",
                    table: "mailbox_mutations",
                    type: "boolean",
                    nullable: true);

                migrationBuilder.AddColumn<string[]>(
                    name: "Keywords",
                    table: "mailbox_mutations",
                    type: "text[]",
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "DesiredFlaggedState",
                    table: "mailbox_mutations");

                migrationBuilder.DropColumn(
                    name: "Keywords",
                    table: "mailbox_mutations");
            }
        }

        /// <summary>The <c>AddStoredEmailMachineAuthorship</c> migration, as it was added.</summary>
        private static class AddStoredEmailMachineAuthorship
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "MachineAuthorshipBand",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "NotAssessed");

                migrationBuilder.AddColumn<double>(
                    name: "MachineAuthorshipLikelihood",
                    table: "stored_emails",
                    type: "double precision",
                    nullable: false,
                    defaultValue: 0.0);

                migrationBuilder.AddColumn<string>(
                    name: "MachineAuthorshipProfileRevision",
                    table: "stored_emails",
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: true);

                migrationBuilder.AddColumn<int>(
                    name: "MachineAuthorshipSignals",
                    table: "stored_emails",
                    type: "integer",
                    nullable: false,
                    defaultValue: 0);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "MachineAuthorshipBand",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "MachineAuthorshipLikelihood",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "MachineAuthorshipProfileRevision",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "MachineAuthorshipSignals",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddJobEnqueuedTraceContext</c> migration, as it was added.</summary>
        private static class AddJobEnqueuedTraceContext
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "EnqueuedTraceParent",
                    table: "jobs",
                    type: "character varying(55)",
                    maxLength: 55,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "EnqueuedTraceState",
                    table: "jobs",
                    type: "character varying(512)",
                    maxLength: 512,
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "EnqueuedTraceParent",
                    table: "jobs");

                migrationBuilder.DropColumn(
                    name: "EnqueuedTraceState",
                    table: "jobs");
            }
        }

        /// <summary>The <c>AddOutgoingEmailLease</c> migration, as it was added.</summary>
        private static class AddOutgoingEmailLease
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                // Npgsql writes the minimum value as `-infinity`, so every row that existed before this column did is due
                // at once rather than at the instant of the upgrade: a send already waiting has waited longer than any
                // backoff this deployment configures.
                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "AvailableAt",
                    table: "outgoing_emails",
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "LeaseExpiresAt",
                    table: "outgoing_emails",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "LeaseOwner",
                    table: "outgoing_emails",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails",
                    columns: new[] { "MailboxAccountId", "AvailableAt", "Id" },
                    filter: "\"Stage\" = 'Recorded'");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails");

                migrationBuilder.DropColumn(
                    name: "AvailableAt",
                    table: "outgoing_emails");

                migrationBuilder.DropColumn(
                    name: "LeaseExpiresAt",
                    table: "outgoing_emails");

                migrationBuilder.DropColumn(
                    name: "LeaseOwner",
                    table: "outgoing_emails");
            }
        }

        /// <summary>The <c>AddMailRederivationRuns</c> migration, as it was added.</summary>
        private static class AddMailRederivationRuns
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "mail_rederivation_runs",
                    columns: table => new
                    {
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        FolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        RunId = table.Column<Guid>(type: "uuid", nullable: false),
                        RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        SegmentCount = table.Column<int>(type: "integer", nullable: false),
                        RederivedEmailCount = table.Column<int>(type: "integer", nullable: false),
                        UnreadableEmailCount = table.Column<int>(type: "integer", nullable: false),
                        MissingContentEmailCount = table.Column<int>(type: "integer", nullable: false),
                        EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_mail_rederivation_runs", x => new { x.MailboxAccountId, x.FolderAlias });
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mail_rederivation_runs");
            }
        }

        /// <summary>The <c>AddOutgoingRecipientContact</c> migration, as it was added.</summary>
        private static class AddOutgoingRecipientContact
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "ContactId",
                    table: "outgoing_email_recipients",
                    type: "uuid",
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "ContactId",
                    table: "outgoing_email_recipients");
            }
        }

        /// <summary>The <c>AddStoredEmailSenderAuthenticationSource</c> migration, as it was added.</summary>
        private static class AddStoredEmailSenderAuthenticationSource
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "SenderAuthenticationSource",
                    table: "stored_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "ReceivingServer");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "SenderAuthenticationSource",
                    table: "stored_emails");
            }
        }

        /// <summary>The <c>AddOutgoingEmailFilings</c> migration, as it was added.</summary>
        private static class AddOutgoingEmailFilings
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails");

                migrationBuilder.AddColumn<Guid>(
                    name: "FiledFromOutgoingEmailId",
                    table: "stored_emails",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<int>(
                    name: "LastFilingFailureCode",
                    table: "outgoing_emails",
                    type: "integer",
                    nullable: true);

                migrationBuilder.CreateTable(
                    name: "outgoing_email_filings",
                    columns: table => new
                    {
                        OutgoingEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        Filing = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        FolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        FolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                        Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        PlacementUidValidity = table.Column<long>(type: "bigint", nullable: true),
                        PlacementUid = table.Column<long>(type: "bigint", nullable: true),
                        InternetMessageId = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                        AppendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_outgoing_email_filings", x => new { x.OutgoingEmailId, x.Filing });
                        table.ForeignKey(
                            name: "fk_outgoing_email_filings_emails",
                            column: x => x.OutgoingEmailId,
                            principalTable: "outgoing_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "Id" },
                    filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings",
                    columns: new[] { "MailboxAccountId", "InternetMessageId" },
                    filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings",
                    columns: new[] { "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "FiledFromOutgoingEmailId",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "LastFilingFailureCode",
                    table: "outgoing_emails");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "Id" },
                    filter: "\"RulesEvaluatedAt\" IS NULL");
            }
        }

        /// <summary>The <c>AddOutgoingEmailPeriodUsageIndex</c> migration, as it was added.</summary>
        private static class AddOutgoingEmailPeriodUsageIndex
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails",
                    columns: new[] { "RecordedAt", "MailboxAccountId" });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails");
            }
        }

        /// <summary>The <c>AddScheduledAndRecurringSends</c> migration, as it was added.</summary>
        private static class AddScheduledAndRecurringSends
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "DueAt",
                    table: "outgoing_emails",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "DueZoneId",
                    table: "outgoing_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: true);

                migrationBuilder.CreateTable(
                    name: "recurring_sends",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Schedule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        DraftByteLength = table.Column<long>(type: "bigint", nullable: false),
                        DeclaredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        LastOccurrenceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        LastOccurrenceEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_recurring_sends", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "recurring_send_drafts",
                    columns: table => new
                    {
                        RecurringSendId = table.Column<Guid>(type: "uuid", nullable: false),
                        DraftMime = table.Column<byte[]>(type: "bytea", nullable: false),
                        DraftByteLength = table.Column<long>(type: "bigint", nullable: false),
                        Sha256Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                        StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_recurring_send_drafts", x => x.RecurringSendId);
                        table.ForeignKey(
                            name: "fk_recurring_send_drafts_sends",
                            column: x => x.RecurringSendId,
                            principalTable: "recurring_sends",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "recurring_send_recipients",
                    columns: table => new
                    {
                        RecurringSendId = table.Column<Guid>(type: "uuid", nullable: false),
                        Ordinal = table.Column<int>(type: "integer", nullable: false),
                        Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                        ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                        Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_recurring_send_recipients", x => new { x.RecurringSendId, x.Ordinal });
                        table.ForeignKey(
                            name: "fk_recurring_send_recipients_sends",
                            column: x => x.RecurringSendId,
                            principalTable: "recurring_sends",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_recurring_sends_active",
                    table: "recurring_sends",
                    column: "DeclaredAt",
                    filter: "\"CancelledAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_recurring_sends_identity",
                    table: "recurring_sends",
                    columns: new[] { "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "recurring_send_drafts");

                migrationBuilder.DropTable(
                    name: "recurring_send_recipients");

                migrationBuilder.DropTable(
                    name: "recurring_sends");

                migrationBuilder.DropColumn(
                    name: "DueAt",
                    table: "outgoing_emails");

                migrationBuilder.DropColumn(
                    name: "DueZoneId",
                    table: "outgoing_emails");
            }
        }

        /// <summary>The <c>AddOutgoingEmailPrincipal</c> migration, as it was added.</summary>
        private static class AddOutgoingEmailPrincipal
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "PrincipalFingerprint",
                    table: "outgoing_emails",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "PrincipalFingerprint",
                    table: "outgoing_emails");
            }
        }

        /// <summary>The <c>AddMailDrafts</c> migration, as it was added.</summary>
        private static class AddMailDrafts
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "mail_drafts",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        RequesterOrigin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        RequesterIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Revision = table.Column<int>(type: "integer", nullable: false),
                        MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                        ComposedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        RevisedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        DiscardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        PromotedToOutgoingEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                        DivergenceReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        DivergenceObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        LastFailureCode = table.Column<int>(type: "integer", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_drafts", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "mail_draft_contents",
                    columns: table => new
                    {
                        MailDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                        RawMime = table.Column<byte[]>(type: "bytea", nullable: false),
                        MimeByteLength = table.Column<long>(type: "bigint", nullable: false),
                        Sha256Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                        StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_draft_contents", x => x.MailDraftId);
                        table.ForeignKey(
                            name: "fk_mail_draft_contents_drafts",
                            column: x => x.MailDraftId,
                            principalTable: "mail_drafts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "mail_draft_copies",
                    columns: table => new
                    {
                        MailDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                        Revision = table.Column<int>(type: "integer", nullable: false),
                        FolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        FolderPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                        Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        PlacementUidValidity = table.Column<long>(type: "bigint", nullable: true),
                        PlacementUid = table.Column<long>(type: "bigint", nullable: true),
                        InternetMessageId = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                        AppendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_draft_copies", x => new { x.MailDraftId, x.Revision });
                        table.ForeignKey(
                            name: "fk_mail_draft_copies_drafts",
                            column: x => x.MailDraftId,
                            principalTable: "mail_drafts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "mail_draft_recipients",
                    columns: table => new
                    {
                        MailDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                        Ordinal = table.Column<int>(type: "integer", nullable: false),
                        Address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                        ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                        Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_draft_recipients", x => new { x.MailDraftId, x.Ordinal });
                        table.ForeignKey(
                            name: "fk_mail_draft_recipients_drafts",
                            column: x => x.MailDraftId,
                            principalTable: "mail_drafts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_drafts_account_revised",
                    table: "mail_drafts",
                    columns: new[] { "MailboxAccountId", "RevisedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_drafts_promoted",
                    table: "mail_drafts",
                    column: "PromotedToOutgoingEmailId",
                    filter: "\"PromotedToOutgoingEmailId\" IS NOT NULL");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mail_draft_contents");

                migrationBuilder.DropTable(
                    name: "mail_draft_copies");

                migrationBuilder.DropTable(
                    name: "mail_draft_recipients");

                migrationBuilder.DropTable(
                    name: "mail_drafts");
            }
        }

        /// <summary>The <c>AddMailDraftRecipientProvenance</c> migration, as it was added.</summary>
        private static class AddMailDraftRecipientProvenance
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Provenance",
                    table: "mail_draft_recipients",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "NamedByCaller");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "Provenance",
                    table: "mail_draft_recipients");
            }
        }

        /// <summary>The <c>AddUserAccounts</c> migration, as it was added.</summary>
        /// <remarks>
        /// The generated shape would have added the user column with the zero identifier as its default and then pointed a
        /// foreign key at a row nothing had inserted, which fails on any database that already holds a mailbox. The three
        /// statements between the generated operations are what make it apply forward instead: the user this deployment is
        /// already serving is provisioned, every mailbox already stored is carried onto it, and only then does the column
        /// become required and keyed. A database holding nothing this chain carries onto a user gets no row at all: a user
        /// is somebody an administrator records, so a fresh deployment starts holding nobody.
        /// </remarks>
        private static class AddUserAccounts
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "settings_accounts",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        Document = table.Column<string>(type: "jsonb", nullable: false),
                        Version = table.Column<long>(type: "bigint", nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_settings_accounts", x => x.Id);
                    });

                // The identifier is generated by the database rather than written into this file, so no two deployments
                // share a user identity and nothing downstream can come to depend on a well-known one. The document
                // starts empty: what belongs in it is the user's mail-account declarations and settings, which are still
                // read from configuration at this release and are imported into it by the change that moves them.
                // The row exists to own what the database already stored, so it is written only where one of the tables
                // this migration and the three after it carry onto the first user holds a row — every one of them exists
                // by now, and a later one filling a column from this row would otherwise meet no row and refuse to make
                // the column required. A fresh database gets none, and its first user is the one an administrator records.
                migrationBuilder.Sql(
                    """
                    INSERT INTO settings_accounts ("Id", "Document", "Version", "CreatedAt", "UpdatedAt")
                    SELECT gen_random_uuid(), '{}'::jsonb, 1, now(), now()
                    WHERE NOT EXISTS (SELECT 1 FROM settings_accounts)
                      AND (EXISTS (SELECT 1 FROM mailbox_accounts)
                        OR EXISTS (SELECT 1 FROM embedding_spend_periods)
                        OR EXISTS (SELECT 1 FROM contacts)
                        OR EXISTS (SELECT 1 FROM email_thread_identifiers)
                        OR EXISTS (SELECT 1 FROM email_threads)
                        OR EXISTS (SELECT 1 FROM mail_answering_audit_entries)
                        OR EXISTS (SELECT 1 FROM mailbox_mutation_audit_entries)
                        OR EXISTS (SELECT 1 FROM mailbox_mutations)
                        OR EXISTS (SELECT 1 FROM mailbox_refresh_tokens)
                        OR EXISTS (SELECT 1 FROM mail_drafts)
                        OR EXISTS (SELECT 1 FROM mail_folders)
                        OR EXISTS (SELECT 1 FROM mail_rederivation_positions)
                        OR EXISTS (SELECT 1 FROM mail_rederivation_runs)
                        OR EXISTS (SELECT 1 FROM mail_rule_evaluation_runs)
                        OR EXISTS (SELECT 1 FROM mail_rule_executions)
                        OR EXISTS (SELECT 1 FROM outgoing_email_filings)
                        OR EXISTS (SELECT 1 FROM outgoing_emails)
                        OR EXISTS (SELECT 1 FROM recurring_sends)
                        OR EXISTS (SELECT 1 FROM spam_classification_runs)
                        OR EXISTS (SELECT 1 FROM stored_emails));
                    """);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mailbox_accounts",
                    type: "uuid",
                    nullable: true);

                // Every mailbox this deployment has synchronized is one user's, because that is what a deployment served
                // before this release. The subquery names the row inserted above rather than a value repeated here.
                migrationBuilder.Sql(
                    """
                    UPDATE mailbox_accounts
                    SET "UserId" = (SELECT "Id" FROM settings_accounts ORDER BY "CreatedAt", "Id" LIMIT 1)
                    WHERE "UserId" IS NULL;
                    """);

                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: "mailbox_accounts",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);

                migrationBuilder.CreateIndex(
                    name: "IX_mailbox_accounts_UserId",
                    table: "mailbox_accounts",
                    column: "UserId");

                migrationBuilder.AddForeignKey(
                    name: "FK_mailbox_accounts_settings_accounts_UserId",
                    table: "mailbox_accounts",
                    column: "UserId",
                    principalTable: "settings_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_mailbox_accounts_settings_accounts_UserId",
                    table: "mailbox_accounts");

                migrationBuilder.DropTable(
                    name: "settings_accounts");

                migrationBuilder.DropIndex(
                    name: "IX_mailbox_accounts_UserId",
                    table: "mailbox_accounts");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mailbox_accounts");
            }
        }

        /// <summary>The <c>AddContentStorageBackendAndObjectLocator</c> migration, as it was added.</summary>
        private static class AddContentStorageBackendAndObjectLocator
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterColumn<byte[]>(
                    name: "DraftMime",
                    table: "recurring_send_drafts",
                    type: "bytea",
                    nullable: true,
                    oldClrType: typeof(byte[]),
                    oldType: "bytea");

                migrationBuilder.AddColumn<string>(
                    name: "Backend",
                    table: "recurring_send_drafts",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "Database");

                migrationBuilder.AddColumn<string>(
                    name: "ObjectLocator",
                    table: "recurring_send_drafts",
                    type: "character varying(1024)",
                    maxLength: 1024,
                    nullable: true);

                migrationBuilder.AlterColumn<byte[]>(
                    name: "RawMime",
                    table: "outgoing_email_contents",
                    type: "bytea",
                    nullable: true,
                    oldClrType: typeof(byte[]),
                    oldType: "bytea");

                migrationBuilder.AddColumn<string>(
                    name: "Backend",
                    table: "outgoing_email_contents",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "Database");

                migrationBuilder.AddColumn<string>(
                    name: "ObjectLocator",
                    table: "outgoing_email_contents",
                    type: "character varying(1024)",
                    maxLength: 1024,
                    nullable: true);

                migrationBuilder.AlterColumn<byte[]>(
                    name: "RawMime",
                    table: "mail_draft_contents",
                    type: "bytea",
                    nullable: true,
                    oldClrType: typeof(byte[]),
                    oldType: "bytea");

                migrationBuilder.AddColumn<string>(
                    name: "Backend",
                    table: "mail_draft_contents",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "Database");

                migrationBuilder.AddColumn<string>(
                    name: "ObjectLocator",
                    table: "mail_draft_contents",
                    type: "character varying(1024)",
                    maxLength: 1024,
                    nullable: true);

                migrationBuilder.AlterColumn<byte[]>(
                    name: "RawMime",
                    table: "email_message_contents",
                    type: "bytea",
                    nullable: true,
                    oldClrType: typeof(byte[]),
                    oldType: "bytea");

                migrationBuilder.AddColumn<string>(
                    name: "Backend",
                    table: "email_message_contents",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "Database");

                migrationBuilder.AddColumn<string>(
                    name: "ObjectLocator",
                    table: "email_message_contents",
                    type: "character varying(1024)",
                    maxLength: 1024,
                    nullable: true);

                migrationBuilder.AddCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts",
                    sql: "(\"Backend\" = 'Database' AND \"DraftMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents");

                migrationBuilder.DropColumn(
                    name: "Backend",
                    table: "recurring_send_drafts");

                migrationBuilder.DropColumn(
                    name: "ObjectLocator",
                    table: "recurring_send_drafts");

                migrationBuilder.DropColumn(
                    name: "Backend",
                    table: "outgoing_email_contents");

                migrationBuilder.DropColumn(
                    name: "ObjectLocator",
                    table: "outgoing_email_contents");

                migrationBuilder.DropColumn(
                    name: "Backend",
                    table: "mail_draft_contents");

                migrationBuilder.DropColumn(
                    name: "ObjectLocator",
                    table: "mail_draft_contents");

                migrationBuilder.DropColumn(
                    name: "Backend",
                    table: "email_message_contents");

                migrationBuilder.DropColumn(
                    name: "ObjectLocator",
                    table: "email_message_contents");

                migrationBuilder.AlterColumn<byte[]>(
                    name: "DraftMime",
                    table: "recurring_send_drafts",
                    type: "bytea",
                    nullable: false,
                    defaultValue: new byte[0],
                    oldClrType: typeof(byte[]),
                    oldType: "bytea",
                    oldNullable: true);

                migrationBuilder.AlterColumn<byte[]>(
                    name: "RawMime",
                    table: "outgoing_email_contents",
                    type: "bytea",
                    nullable: false,
                    defaultValue: new byte[0],
                    oldClrType: typeof(byte[]),
                    oldType: "bytea",
                    oldNullable: true);

                migrationBuilder.AlterColumn<byte[]>(
                    name: "RawMime",
                    table: "mail_draft_contents",
                    type: "bytea",
                    nullable: false,
                    defaultValue: new byte[0],
                    oldClrType: typeof(byte[]),
                    oldType: "bytea",
                    oldNullable: true);

                migrationBuilder.AlterColumn<byte[]>(
                    name: "RawMime",
                    table: "email_message_contents",
                    type: "bytea",
                    nullable: false,
                    defaultValue: new byte[0],
                    oldClrType: typeof(byte[]),
                    oldType: "bytea",
                    oldNullable: true);
            }
        }

        /// <summary>The <c>AddJobTurn</c> migration, as it was added.</summary>
        private static class AddJobTurn
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_jobs_claimable",
                    table: "jobs");

                // Added nullable and filled rather than defaulted, because the default EF composes for a non-null instant
                // is the minimum one, which Npgsql writes as -infinity: every job this deployment already had waiting
                // would hold a turn before every job it will ever enqueue, and the queue would drain its backlog in the
                // order it was going to drain it anyway.
                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "TurnAt",
                    table: "jobs",
                    type: "timestamp with time zone",
                    nullable: true);

                // The instant a job became available is the turn it already held: before this migration that column was
                // the whole of the claim's order, so filling it this way is the queue keeping the order it has rather than
                // being reshuffled by an upgrade. Fairness begins with what is enqueued afterwards.
                migrationBuilder.Sql(
                    """
                    UPDATE jobs
                    SET "TurnAt" = "AvailableAt"
                    WHERE "TurnAt" IS NULL;
                    """);

                migrationBuilder.AlterColumn<DateTimeOffset>(
                    name: "TurnAt",
                    table: "jobs",
                    type: "timestamp with time zone",
                    nullable: false,
                    oldClrType: typeof(DateTimeOffset),
                    oldType: "timestamp with time zone",
                    oldNullable: true);

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_account_turn",
                    table: "jobs",
                    columns: new[] { "MailboxAccountId", "TurnAt" },
                    filter: "\"State\" IN ('Pending', 'Claimed')");

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_claimable",
                    table: "jobs",
                    columns: new[] { "JobType", "TurnAt" },
                    filter: "\"State\" IN ('Pending', 'Claimed')");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_jobs_account_turn",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "ix_jobs_claimable",
                    table: "jobs");

                migrationBuilder.DropColumn(
                    name: "TurnAt",
                    table: "jobs");

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_claimable",
                    table: "jobs",
                    columns: new[] { "JobType", "AvailableAt" },
                    filter: "\"State\" IN ('Pending', 'Claimed')");
            }
        }

        /// <summary>The <c>BoundEmbeddingSpendAndStoredContentPerUser</c> migration, as it was added.</summary>
        private static class BoundEmbeddingSpendAndStoredContentPerUser
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropPrimaryKey(
                    name: "PK_embedding_spend_periods",
                    table: "embedding_spend_periods");

                // Added nullable and filled before it is closed, rather than with a default: a default would attribute
                // every period this deployment has already spent to nobody, and the zero identifier is not a row anybody
                // could later reconcile against.
                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "embedding_spend_periods",
                    type: "uuid",
                    nullable: true);

                // Every character this deployment has spent was spent for one user, because that is what a deployment
                // served before the release that introduced the user record. The subquery names that row rather than a
                // value repeated here, exactly as the migration which carried the mail accounts onto it does.
                migrationBuilder.Sql(
                    """
                    UPDATE embedding_spend_periods
                    SET "UserId" = (SELECT "Id" FROM settings_accounts ORDER BY "CreatedAt", "Id" LIMIT 1)
                    WHERE "UserId" IS NULL;
                    """);

                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: "embedding_spend_periods",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);

                migrationBuilder.AddPrimaryKey(
                    name: "PK_embedding_spend_periods",
                    table: "embedding_spend_periods",
                    columns: new[] { "PeriodStartsAt", "UserId" });

                migrationBuilder.CreateTable(
                    name: "user_stored_content",
                    columns: table => new
                    {
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        StoredContentByteCount = table.Column<long>(type: "bigint", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_user_stored_content", x => x.UserId);
                        table.ForeignKey(
                            name: "FK_user_stored_content_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                // The counter starts from what the payloads already hold rather than from zero, because the ceiling it
                // serves is consulted before every message: a user whose mailbox was already stored would otherwise be
                // admitted a second mailbox's worth on the strength of a figure that only meant nobody had written it. This
                // is the same sum the port re-derives with, and it is one statement over the whole deployment rather than
                // one per user. A user holding no content at all gets no row and is derived on their first read. It scans
                // the content table once, reading the recorded length rather than the payload beside it, so a large mailbox
                // costs a sequential scan at upgrade and detoasts nothing.
                migrationBuilder.Sql(
                    """
                    INSERT INTO user_stored_content ("UserId", "StoredContentByteCount")
                    SELECT account."UserId", SUM(content."MimeByteLength")
                    FROM email_message_contents AS content
                    JOIN stored_emails AS email
                        ON email."Id" = content."StoredEmailId"
                    JOIN mailbox_accounts AS account
                        ON account."Id" = email."MailboxAccountId"
                    GROUP BY account."UserId";
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "user_stored_content");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_embedding_spend_periods",
                    table: "embedding_spend_periods");

                // What each user spent is added onto one row per period before the others are removed, because the key
                // restored below cannot hold two rows naming the same period: dropping the column first would leave the
                // schema unable to take its own primary key back on any deployment that served more than one user. The
                // row kept is the one whose user sorts first, which makes the choice deterministic rather than whichever
                // the server returned.
                migrationBuilder.Sql(
                    """
                    UPDATE embedding_spend_periods AS survivor
                    SET "ConsumedInputCharacterCount" = totals.total
                    FROM (
                        SELECT "PeriodStartsAt", SUM("ConsumedInputCharacterCount") AS total
                        FROM embedding_spend_periods
                        GROUP BY "PeriodStartsAt"
                    ) AS totals
                    WHERE survivor."PeriodStartsAt" = totals."PeriodStartsAt"
                      AND survivor."UserId" = (
                          SELECT charged."UserId"
                          FROM embedding_spend_periods AS charged
                          WHERE charged."PeriodStartsAt" = survivor."PeriodStartsAt"
                          ORDER BY charged."UserId"
                          LIMIT 1);
                    """);

                migrationBuilder.Sql(
                    """
                    DELETE FROM embedding_spend_periods AS superseded
                    WHERE superseded."UserId" <> (
                        SELECT charged."UserId"
                        FROM embedding_spend_periods AS charged
                        WHERE charged."PeriodStartsAt" = superseded."PeriodStartsAt"
                        ORDER BY charged."UserId"
                        LIMIT 1);
                    """);

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "embedding_spend_periods");

                migrationBuilder.AddPrimaryKey(
                    name: "PK_embedding_spend_periods",
                    table: "embedding_spend_periods",
                    column: "PeriodStartsAt");
            }
        }

        /// <summary>The <c>IndexObjectBackedContentAndRequireItsPayloadEmpty</c> migration, as it was added.</summary>
        private static class IndexObjectBackedContentAndRequireItsPayloadEmpty
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents");

                migrationBuilder.CreateIndex(
                    name: "ix_recurring_send_drafts_object_backed",
                    table: "recurring_send_drafts",
                    column: "Backend",
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts",
                    sql: "(\"Backend\" = 'Database' AND \"DraftMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"DraftMime\" IS NULL)");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_contents_object_backed",
                    table: "outgoing_email_contents",
                    column: "Backend",
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");

                migrationBuilder.CreateIndex(
                    name: "ix_mail_draft_contents_object_backed",
                    table: "mail_draft_contents",
                    column: "Backend",
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");

                migrationBuilder.CreateIndex(
                    name: "ix_email_message_contents_object_backed",
                    table: "email_message_contents",
                    column: "Backend",
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_recurring_send_drafts_object_backed",
                    table: "recurring_send_drafts");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_contents_object_backed",
                    table: "outgoing_email_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents");

                migrationBuilder.DropIndex(
                    name: "ix_mail_draft_contents_object_backed",
                    table: "mail_draft_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents");

                migrationBuilder.DropIndex(
                    name: "ix_email_message_contents_object_backed",
                    table: "email_message_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts",
                    sql: "(\"Backend\" = 'Database' AND \"DraftMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL)");
            }
        }

        // Authored against a base that did not yet carry 20260824190011_IndexContentObjectLocators, so this identifier
        // sorts before a migration that merged first and is never the newest one a filename sort finds. It is permanent,
        // because a database has already written it into __EFMigrationsHistory, and the ordering costs nothing at runtime:
        // the two touch different tables and either order leaves one schema. What it does cost is
        // scripts/script-migration.sh with no arguments, which reads that sort and would script the locator index instead.
        // Review this one's SQL by naming the pair:
        //   scripts/script-migration.sh 20260824142528_IndexObjectBackedContentAndRequireItsPayloadEmpty 20260824152343_AddContactUser

        /// <summary>The <c>AddContactUser</c> migration, as it was added.</summary>
        /// <remarks>
        /// <para>
        /// The generated shape would have added both user columns with the zero identifier as their default and then
        /// pointed a foreign key at a row nothing had inserted, which fails on any database that already holds a contact.
        /// The statements between the generated operations are what make it apply forward instead: the columns arrive
        /// nullable, every contact already written is carried onto the user this deployment is already serving, every
        /// address row takes the user of the contact it hangs on, and only then do the columns become required and keyed.
        /// The user row itself is <c>AddUserAccounts</c>' work and always exists by the time this runs.
        /// </para>
        /// <para>
        /// The uniqueness over an address narrows from the whole table to one user's book, so nothing that was accepted
        /// before is refused now and no row has to be reconciled to apply this. Reverting it is the direction that cannot
        /// be taken freely: a book that has since acquired two users holding one address between them has no
        /// deployment-wide unique index to go back to, and <c>Down</c> fails on the index rather than discarding one of
        /// them.
        /// </para>
        /// </remarks>
        private static class AddContactUser
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_contact_addresses_contacts_ContactId",
                    table: "contact_addresses");

                migrationBuilder.DropIndex(
                    name: "ix_contacts_display_name_sort_key_id",
                    table: "contacts");

                migrationBuilder.DropIndex(
                    name: "IX_contact_addresses_ContactId",
                    table: "contact_addresses");

                migrationBuilder.DropIndex(
                    name: "ix_contact_addresses_normalized_address",
                    table: "contact_addresses");

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "contacts",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "contact_addresses",
                    type: "uuid",
                    nullable: true);

                // Every contact this deployment holds is one user's, because that is what a deployment served before this
                // release. The subquery names the row AddUserAccounts inserted rather than a value repeated here.
                migrationBuilder.Sql(
                    """
                    UPDATE contacts
                    SET "UserId" = (SELECT "Id" FROM settings_accounts ORDER BY "CreatedAt", "Id" LIMIT 1)
                    WHERE "UserId" IS NULL;
                    """);

                // An address row's user is the user of the contact it hangs on, which is what the composite key below
                // then makes structural rather than a value this statement got right once.
                migrationBuilder.Sql(
                    """
                    UPDATE contact_addresses
                    SET "UserId" = contacts."UserId"
                    FROM contacts
                    WHERE contacts."Id" = contact_addresses."ContactId"
                      AND contact_addresses."UserId" IS NULL;
                    """);

                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: "contacts",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);

                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: "contact_addresses",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);

                migrationBuilder.AddUniqueConstraint(
                    name: "AK_contacts_Id_UserId",
                    table: "contacts",
                    columns: new[] { "Id", "UserId" });

                migrationBuilder.CreateIndex(
                    name: "ix_contacts_user_display_name_sort_key_id",
                    table: "contacts",
                    columns: new[] { "UserId", "DisplayNameSortKey", "Id" });

                migrationBuilder.CreateIndex(
                    name: "IX_contact_addresses_ContactId_UserId",
                    table: "contact_addresses",
                    columns: new[] { "ContactId", "UserId" });

                migrationBuilder.CreateIndex(
                    name: "ix_contact_addresses_user_normalized_address",
                    table: "contact_addresses",
                    columns: new[] { "UserId", "NormalizedAddress" },
                    unique: true);

                migrationBuilder.AddForeignKey(
                    name: "FK_contact_addresses_contacts_ContactId_UserId",
                    table: "contact_addresses",
                    columns: new[] { "ContactId", "UserId" },
                    principalTable: "contacts",
                    principalColumns: new[] { "Id", "UserId" },
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_contacts_settings_accounts_UserId",
                    table: "contacts",
                    column: "UserId",
                    principalTable: "settings_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_contact_addresses_contacts_ContactId_UserId",
                    table: "contact_addresses");

                migrationBuilder.DropForeignKey(
                    name: "FK_contacts_settings_accounts_UserId",
                    table: "contacts");

                migrationBuilder.DropUniqueConstraint(
                    name: "AK_contacts_Id_UserId",
                    table: "contacts");

                migrationBuilder.DropIndex(
                    name: "ix_contacts_user_display_name_sort_key_id",
                    table: "contacts");

                migrationBuilder.DropIndex(
                    name: "IX_contact_addresses_ContactId_UserId",
                    table: "contact_addresses");

                migrationBuilder.DropIndex(
                    name: "ix_contact_addresses_user_normalized_address",
                    table: "contact_addresses");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "contacts");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "contact_addresses");

                migrationBuilder.CreateIndex(
                    name: "ix_contacts_display_name_sort_key_id",
                    table: "contacts",
                    columns: new[] { "DisplayNameSortKey", "Id" });

                migrationBuilder.CreateIndex(
                    name: "IX_contact_addresses_ContactId",
                    table: "contact_addresses",
                    column: "ContactId");

                migrationBuilder.CreateIndex(
                    name: "ix_contact_addresses_normalized_address",
                    table: "contact_addresses",
                    column: "NormalizedAddress",
                    unique: true);

                migrationBuilder.AddForeignKey(
                    name: "FK_contact_addresses_contacts_ContactId",
                    table: "contact_addresses",
                    column: "ContactId",
                    principalTable: "contacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
        }

        /// <summary>The <c>IndexContentObjectLocators</c> migration, as it was added.</summary>
        private static class IndexContentObjectLocators
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_recurring_send_drafts_object_backed",
                    table: "recurring_send_drafts");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_contents_object_backed",
                    table: "outgoing_email_contents");

                migrationBuilder.DropIndex(
                    name: "ix_mail_draft_contents_object_backed",
                    table: "mail_draft_contents");

                migrationBuilder.DropIndex(
                    name: "ix_email_message_contents_object_backed",
                    table: "email_message_contents");

                migrationBuilder.CreateIndex(
                    name: "ix_recurring_send_drafts_object_locator",
                    table: "recurring_send_drafts",
                    column: "ObjectLocator",
                    unique: true,
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_contents_object_locator",
                    table: "outgoing_email_contents",
                    column: "ObjectLocator",
                    unique: true,
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.CreateIndex(
                    name: "ix_mail_draft_contents_object_locator",
                    table: "mail_draft_contents",
                    column: "ObjectLocator",
                    unique: true,
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.CreateIndex(
                    name: "ix_email_message_contents_object_locator",
                    table: "email_message_contents",
                    column: "ObjectLocator",
                    unique: true,
                    filter: "\"Backend\" = 'ObjectStorage'");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_recurring_send_drafts_object_locator",
                    table: "recurring_send_drafts");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_contents_object_locator",
                    table: "outgoing_email_contents");

                migrationBuilder.DropIndex(
                    name: "ix_mail_draft_contents_object_locator",
                    table: "mail_draft_contents");

                migrationBuilder.DropIndex(
                    name: "ix_email_message_contents_object_locator",
                    table: "email_message_contents");

                migrationBuilder.CreateIndex(
                    name: "ix_recurring_send_drafts_object_backed",
                    table: "recurring_send_drafts",
                    column: "Backend",
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_contents_object_backed",
                    table: "outgoing_email_contents",
                    column: "Backend",
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.CreateIndex(
                    name: "ix_mail_draft_contents_object_backed",
                    table: "mail_draft_contents",
                    column: "Backend",
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.CreateIndex(
                    name: "ix_email_message_contents_object_backed",
                    table: "email_message_contents",
                    column: "Backend",
                    filter: "\"Backend\" = 'ObjectStorage'");
            }
        }

        /// <summary>The <c>AddContentMoveRun</c> migration, as it was added.</summary>
        private static class AddContentMoveRun
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "content_move_runs",
                    columns: table => new
                    {
                        Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        ResumeAfter = table.Column<Guid>(type: "uuid", nullable: true),
                        CopiedPayloadCount = table.Column<long>(type: "bigint", nullable: false),
                        FailedPayloadCount = table.Column<long>(type: "bigint", nullable: false),
                        MovedByteCount = table.Column<long>(type: "bigint", nullable: false),
                        EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_content_move_runs", x => x.Name);
                        table.CheckConstraint("ck_content_move_runs_singleton", "\"Name\" = 'stored-content'");
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "content_move_runs");
            }
        }

        /// <summary>The <c>RetainDatabaseCopyUntilReleased</c> migration, as it was added.</summary>
        private static class RetainDatabaseCopyUntilReleased
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents");

                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "ObjectVerifiedAt",
                    table: "recurring_send_drafts",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "ObjectVerifiedAt",
                    table: "outgoing_email_contents",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "ObjectVerifiedAt",
                    table: "mail_draft_contents",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "ObjectVerifiedAt",
                    table: "email_message_contents",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts",
                    sql: "(\"Backend\" = 'Database' AND \"DraftMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"DraftMime\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"RawMime\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"RawMime\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"RawMime\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents");

                migrationBuilder.DropColumn(
                    name: "ObjectVerifiedAt",
                    table: "recurring_send_drafts");

                migrationBuilder.DropColumn(
                    name: "ObjectVerifiedAt",
                    table: "outgoing_email_contents");

                migrationBuilder.DropColumn(
                    name: "ObjectVerifiedAt",
                    table: "mail_draft_contents");

                migrationBuilder.DropColumn(
                    name: "ObjectVerifiedAt",
                    table: "email_message_contents");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_recurring_send_drafts_backend_payload",
                    table: "recurring_send_drafts",
                    sql: "(\"Backend\" = 'Database' AND \"DraftMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"DraftMime\" IS NULL)");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_outgoing_email_contents_backend_payload",
                    table: "outgoing_email_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_mail_draft_contents_backend_payload",
                    table: "mail_draft_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_email_message_contents_backend_payload",
                    table: "email_message_contents",
                    sql: "(\"Backend\" = 'Database' AND \"RawMime\" IS NOT NULL AND \"ObjectLocator\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL AND \"RawMime\" IS NULL)");
            }
        }

        /// <summary>The <c>AddRootSettings</c> migration, as it was added.</summary>
        /// <remarks>
        /// The row is provisioned by the migration rather than by the first write, so the layer the host composes its
        /// settings over exists on every database this build runs against. An empty document contributes no configuration
        /// key at all, which is the state a deployment that has persisted nothing is in — and is exactly what the layer
        /// means by a key it does not carry: inherit from the source beneath it.
        /// </remarks>
        private static class AddRootSettings
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "settings_root",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "integer", nullable: false),
                        Document = table.Column<string>(type: "jsonb", nullable: false),
                        Version = table.Column<long>(type: "bigint", nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_settings_root", x => x.Id);
                        table.CheckConstraint("ck_settings_root_singleton", "\"Id\" = 1");
                    });

                // Guarded rather than unconditional so that re-applying the migration set to a database that already
                // carries the row is the no-op an operator expects, and the check constraint keeps a second one out.
                migrationBuilder.Sql(
                    """
                    INSERT INTO settings_root ("Id", "Document", "Version", "CreatedAt", "UpdatedAt")
                    SELECT 1, '{}'::jsonb, 1, now(), now()
                    WHERE NOT EXISTS (SELECT 1 FROM settings_root);
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "settings_root");
            }
        }

        /// <summary>The <c>CarryUserBesideMailAccountReferences</c> migration, as it was added.</summary>
        /// <remarks>
        /// <para>
        /// Every table that references a mail account gains the user that account belongs to, and every index that led
        /// with the account identifier is replaced by one leading with the pair. An identifier is the name an operator
        /// wrote and names one account within its user, so a structure led by the identifier alone would interleave two
        /// users' rows under one key once a deployment serves more than one person.
        /// </para>
        /// <para>
        /// The generated shape would have added each column as required with the zero identifier as its default, which
        /// silently attributes every row a database already holds to a user that does not exist. The statements between
        /// the generated operations are what make it apply forward instead: the columns arrive nullable, every row is
        /// carried onto the user of the account it names, whatever is left is carried onto the one user the deployment
        /// was serving, and only then does the column become required. <c>jobs</c> keeps a nullable user, because its
        /// account reference is nullable and a queue row about no account belongs to nobody.
        /// </para>
        /// <para>
        /// <c>mailbox_accounts</c> keeps its single-column key, so no identifier that was legal before this migration is
        /// refused after it. Reverting is free of data loss in a way the forward direction is not: <c>Down</c> drops the
        /// columns and restores every index the account alone led, which is a schema a single-user deployment is still
        /// correct under.
        /// </para>
        /// </remarks>
        private static class CarryUserBesideMailAccountReferences
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_account_identity",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_account_timeline",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_recurring_sends_identity",
                    table: "recurring_sends");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_identity",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_outstanding",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_outstanding",
                    table: "mailbox_mutations");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutation_audit_entries_account_completed",
                    table: "mailbox_mutation_audit_entries");

                migrationBuilder.DropIndex(
                    name: "ix_mail_rule_executions_account_evaluated",
                    table: "mail_rule_executions");

                migrationBuilder.DropIndex(
                    name: "ix_mail_rule_executions_account_rule_evaluated",
                    table: "mail_rule_executions");

                migrationBuilder.DropIndex(
                    name: "ix_mail_folders_account_alias_generation",
                    table: "mail_folders");

                migrationBuilder.DropIndex(
                    name: "ix_mail_drafts_account_revised",
                    table: "mail_drafts");

                migrationBuilder.DropIndex(
                    name: "ix_mail_answering_audit_entries_account_completed",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropIndex(
                    name: "ix_mail_answering_audit_entries_run_account",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropIndex(
                    name: "ix_jobs_account",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "ix_jobs_account_turn",
                    table: "jobs");

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "stored_emails",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "spam_classification_runs",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "recurring_sends",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "outgoing_emails",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "outgoing_email_filings",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mailbox_refresh_tokens",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mailbox_mutations",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mailbox_mutation_audit_entries",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_rule_executions",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_rule_evaluation_runs",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_rederivation_runs",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_rederivation_positions",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_folders",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_drafts",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_answering_audit_entries",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "jobs",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "email_threads",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "email_thread_identifiers",
                    type: "uuid",
                    nullable: true);

                // Every row that names an account takes that account's user, which is the column AddUserAccounts put on
                // mailbox_accounts and #981 filled. Two tables are carried from the row they hang off instead, because
                // that association is a foreign key while the account identifier beside it is a copy: a stored email
                // through its folder binding, and a thread identifier through its thread.
                FillUserFromAccount(migrationBuilder, "mail_folders");
                FillUserFromAccount(migrationBuilder, "email_threads");
                FillUserFromAccount(migrationBuilder, "mail_drafts");
                FillUserFromAccount(migrationBuilder, "mail_answering_audit_entries");
                FillUserFromAccount(migrationBuilder, "mail_rederivation_positions");
                FillUserFromAccount(migrationBuilder, "mail_rederivation_runs");
                FillUserFromAccount(migrationBuilder, "mail_rule_evaluation_runs");
                FillUserFromAccount(migrationBuilder, "mail_rule_executions");
                FillUserFromAccount(migrationBuilder, "mailbox_mutation_audit_entries");
                FillUserFromAccount(migrationBuilder, "mailbox_mutations");
                FillUserFromAccount(migrationBuilder, "mailbox_refresh_tokens");
                FillUserFromAccount(migrationBuilder, "outgoing_email_filings");
                FillUserFromAccount(migrationBuilder, "outgoing_emails");
                FillUserFromAccount(migrationBuilder, "recurring_sends");
                FillUserFromAccount(migrationBuilder, "spam_classification_runs");
                FillUserFromAccount(migrationBuilder, "jobs");

                migrationBuilder.Sql(
                    """
                    UPDATE stored_emails
                    SET "UserId" = mail_folders."UserId"
                    FROM mail_folders
                    WHERE mail_folders."Id" = stored_emails."MailFolderId"
                      AND stored_emails."UserId" IS NULL;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE email_thread_identifiers
                    SET "UserId" = email_threads."UserId"
                    FROM email_threads
                    WHERE email_threads."Id" = email_thread_identifiers."EmailThreadId"
                      AND email_thread_identifiers."UserId" IS NULL;
                    """);

                // What is left names an account this deployment no longer holds a row for — a mailbox removed from the
                // configuration keeps the history it produced, and only three of these tables key into mailbox_accounts at
                // all. Such a row belongs to the one user the deployment was serving when it was written, which is the
                // invariant IDeploymentUserSource states and which #1248 is what ends. The subquery names the row
                // AddUserAccounts inserted rather than a value repeated here.
                FillUserFromDeployment(migrationBuilder, "mail_drafts");
                FillUserFromDeployment(migrationBuilder, "mail_answering_audit_entries");
                FillUserFromDeployment(migrationBuilder, "mail_rederivation_positions");
                FillUserFromDeployment(migrationBuilder, "mail_rederivation_runs");
                FillUserFromDeployment(migrationBuilder, "mail_rule_evaluation_runs");
                FillUserFromDeployment(migrationBuilder, "mail_rule_executions");
                FillUserFromDeployment(migrationBuilder, "mailbox_mutation_audit_entries");
                FillUserFromDeployment(migrationBuilder, "mailbox_mutations");
                FillUserFromDeployment(migrationBuilder, "mailbox_refresh_tokens");
                FillUserFromDeployment(migrationBuilder, "outgoing_email_filings");
                FillUserFromDeployment(migrationBuilder, "outgoing_emails");
                FillUserFromDeployment(migrationBuilder, "recurring_sends");
                FillUserFromDeployment(migrationBuilder, "spam_classification_runs");
                FillUserFromDeployment(migrationBuilder, "email_thread_identifiers");
                FillUserFromDeployment(migrationBuilder, "stored_emails");
                FillUserFromDeployment(migrationBuilder, "email_threads");
                FillUserFromDeployment(migrationBuilder, "mail_folders");

                // Required only now, and on every table but jobs: a queue row may belong to no account at all, so its
                // user is absent for exactly the rows its account is.
                RequireUser(migrationBuilder, "email_thread_identifiers");
                RequireUser(migrationBuilder, "email_threads");
                RequireUser(migrationBuilder, "mail_answering_audit_entries");
                RequireUser(migrationBuilder, "mail_drafts");
                RequireUser(migrationBuilder, "mail_folders");
                RequireUser(migrationBuilder, "mail_rederivation_positions");
                RequireUser(migrationBuilder, "mail_rederivation_runs");
                RequireUser(migrationBuilder, "mail_rule_evaluation_runs");
                RequireUser(migrationBuilder, "mail_rule_executions");
                RequireUser(migrationBuilder, "mailbox_mutation_audit_entries");
                RequireUser(migrationBuilder, "mailbox_mutations");
                RequireUser(migrationBuilder, "mailbox_refresh_tokens");
                RequireUser(migrationBuilder, "outgoing_email_filings");
                RequireUser(migrationBuilder, "outgoing_emails");
                RequireUser(migrationBuilder, "recurring_sends");
                RequireUser(migrationBuilder, "spam_classification_runs");
                RequireUser(migrationBuilder, "stored_emails");

                // The queued documents are carried across too, and this is the one place they can be. Every payload that
                // names an account now names its user as well and declares the property required, so a document written by
                // the previous release no longer parses — and the claim reads a batch of rows rather than one, so a single
                // unparseable document would take every job claimed beside it with it, on a queue that would then never
                // drain. The user is on the row by the statements above, so the value is already here; a payload that
                // names no account has no user to write and is left alone.
                migrationBuilder.Sql(
                    """
                    UPDATE jobs
                    SET "Payload" = jsonb_set("Payload", '{userId}', to_jsonb("UserId"))
                    WHERE "UserId" IS NOT NULL
                      AND NOT jsonb_exists("Payload", 'userId');
                    """);

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "Id" },
                    filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_user_account_identity",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_user_account_timeline",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "ReceivedAt", "Id" },
                    descending: new[] { false, false, true, true })
                    .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

                migrationBuilder.CreateIndex(
                    name: "ix_recurring_sends_identity",
                    table: "recurring_sends",
                    columns: new[] { "UserId", "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "AvailableAt", "Id" },
                    filter: "\"Stage\" = 'Recorded'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_identity",
                    table: "outgoing_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_outstanding",
                    table: "outgoing_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" NOT IN ('Sent', 'Refused', 'Cancelled')");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails",
                    columns: new[] { "RecordedAt", "UserId", "MailboxAccountId" });

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                    filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_outstanding",
                    table: "mailbox_mutations",
                    columns: new[] { "UserId", "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" <> 'Completed'");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations",
                    columns: new[] { "UserId", "MailboxAccountId", "DestinationFolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"PlacementObservedAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutation_audit_entries_user_account_completed",
                    table: "mailbox_mutation_audit_entries",
                    columns: new[] { "UserId", "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_user_account_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "UserId", "MailboxAccountId", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_user_account_rule_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "UserId", "MailboxAccountId", "RuleName", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "IX_mail_folders_MailboxAccountId",
                    table: "mail_folders",
                    column: "MailboxAccountId");

                migrationBuilder.CreateIndex(
                    name: "ix_mail_folders_user_account_alias_generation",
                    table: "mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId", "Alias", "ResolutionGeneration" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mail_drafts_user_account_revised",
                    table: "mail_drafts",
                    columns: new[] { "UserId", "MailboxAccountId", "RevisedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_user_account_completed",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "UserId", "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_run_user_account",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "RunId", "UserId", "MailboxAccountId" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "IX_jobs_MailboxAccountId",
                    table: "jobs",
                    column: "MailboxAccountId");

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_user_account",
                    table: "jobs",
                    columns: new[] { "UserId", "MailboxAccountId", "EnqueuedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_user_turn",
                    table: "jobs",
                    columns: new[] { "UserId", "TurnAt" },
                    filter: "\"State\" IN ('Pending', 'Claimed')");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                // The payload rewrite in Up has no mirror here, deliberately. The contract it wrote into is source-generated
                // with the default unmapped-member handling, so a release without the user reads a document carrying one
                // and ignores the property; taking it back out would rewrite every queued document to no effect, and a
                // second Up would then have to write it again.
                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_user_account_identity",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_user_account_timeline",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_recurring_sends_identity",
                    table: "recurring_sends");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_identity",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_outstanding",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_outstanding",
                    table: "mailbox_mutations");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutation_audit_entries_user_account_completed",
                    table: "mailbox_mutation_audit_entries");

                migrationBuilder.DropIndex(
                    name: "ix_mail_rule_executions_user_account_evaluated",
                    table: "mail_rule_executions");

                migrationBuilder.DropIndex(
                    name: "ix_mail_rule_executions_user_account_rule_evaluated",
                    table: "mail_rule_executions");

                migrationBuilder.DropIndex(
                    name: "IX_mail_folders_MailboxAccountId",
                    table: "mail_folders");

                migrationBuilder.DropIndex(
                    name: "ix_mail_folders_user_account_alias_generation",
                    table: "mail_folders");

                migrationBuilder.DropIndex(
                    name: "ix_mail_drafts_user_account_revised",
                    table: "mail_drafts");

                migrationBuilder.DropIndex(
                    name: "ix_mail_answering_audit_entries_user_account_completed",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropIndex(
                    name: "ix_mail_answering_audit_entries_run_user_account",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropIndex(
                    name: "IX_jobs_MailboxAccountId",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "ix_jobs_user_account",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "ix_jobs_user_turn",
                    table: "jobs");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "spam_classification_runs");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "recurring_sends");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "outgoing_emails");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "outgoing_email_filings");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mailbox_refresh_tokens");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mailbox_mutations");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mailbox_mutation_audit_entries");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_rule_executions");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_rule_evaluation_runs");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_rederivation_runs");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_rederivation_positions");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_folders");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_drafts");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "jobs");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "email_threads");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "email_thread_identifiers");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_account_identity",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_account_timeline",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "ReceivedAt", "Id" },
                    descending: new[] { false, true, true })
                    .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "Id" },
                    filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_recurring_sends_identity",
                    table: "recurring_sends",
                    columns: new[] { "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails",
                    columns: new[] { "MailboxAccountId", "AvailableAt", "Id" },
                    filter: "\"Stage\" = 'Recorded'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_identity",
                    table: "outgoing_emails",
                    columns: new[] { "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_outstanding",
                    table: "outgoing_emails",
                    columns: new[] { "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" NOT IN ('Sent', 'Refused', 'Cancelled')");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails",
                    columns: new[] { "RecordedAt", "MailboxAccountId" });

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings",
                    columns: new[] { "MailboxAccountId", "InternetMessageId" },
                    filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings",
                    columns: new[] { "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_outstanding",
                    table: "mailbox_mutations",
                    columns: new[] { "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" <> 'Completed'");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations",
                    columns: new[] { "MailboxAccountId", "DestinationFolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"PlacementObservedAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutation_audit_entries_account_completed",
                    table: "mailbox_mutation_audit_entries",
                    columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_account_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "MailboxAccountId", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_account_rule_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "MailboxAccountId", "RuleName", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_folders_account_alias_generation",
                    table: "mail_folders",
                    columns: new[] { "MailboxAccountId", "Alias", "ResolutionGeneration" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mail_drafts_account_revised",
                    table: "mail_drafts",
                    columns: new[] { "MailboxAccountId", "RevisedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_account_completed",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_run_account",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "RunId", "MailboxAccountId" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_account",
                    table: "jobs",
                    columns: new[] { "MailboxAccountId", "EnqueuedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_account_turn",
                    table: "jobs",
                    columns: new[] { "MailboxAccountId", "TurnAt" },
                    filter: "\"State\" IN ('Pending', 'Claimed')");
            }

            /// <summary>Carries every row of one table onto the user of the account it names.</summary>
            /// <remarks>
            /// One statement per table rather than one over a union, because each is a separate scan the database can
            /// report on its own and because the tables are of wildly different sizes. Rows already filled are skipped, so
            /// re-running a half-applied statement costs a scan rather than overwriting an answer.
            /// </remarks>
            private static void FillUserFromAccount(MigrationBuilder migrationBuilder, string table) =>
                migrationBuilder.Sql(
                    $"""
                     UPDATE {table}
                     SET "UserId" = mailbox_accounts."UserId"
                     FROM mailbox_accounts
                     WHERE mailbox_accounts."Id" = {table}."MailboxAccountId"
                       AND {table}."UserId" IS NULL;
                     """);

            /// <summary>Carries whatever is left onto the one user this deployment was serving.</summary>
            private static void FillUserFromDeployment(MigrationBuilder migrationBuilder, string table) =>
                migrationBuilder.Sql(
                    $"""
                     UPDATE {table}
                     SET "UserId" = (SELECT "Id" FROM settings_accounts ORDER BY "CreatedAt", "Id" LIMIT 1)
                     WHERE "UserId" IS NULL;
                     """);

            /// <summary>Makes the filled column required, which is what the reads and the indexes above depend on.</summary>
            private static void RequireUser(MigrationBuilder migrationBuilder, string table) =>
                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: table,
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);
        }

        /// <summary>The <c>KeyMailAccountByUserAndIdentifier</c> migration, as it was added.</summary>
        /// <remarks>
        /// <para>
        /// A mail account is keyed by its user and the identifier its operator chose, so the identifier names one mailbox
        /// within its user and nothing across the deployment. Every foreign key onto the account becomes the pair, and the
        /// six keys that were led by the account identifier gain the user in front of it. Nothing is renamed, no column is
        /// added or dropped, and no value is rewritten: the columns this reorders were put there and filled by
        /// <c>CarryUserBesideMailAccountReferences</c>, so this migration is a key change over data that already agrees
        /// with it.
        /// </para>
        /// <para>
        /// It applies forward without a fill for the same reason. Every row on the three tables keying onto the account
        /// took its user from the account it names, so each pair already resolves and the foreign keys validate rather
        /// than fail; the user on a queue row is present for exactly the rows an account is, which is what the new check
        /// states rather than establishes.
        /// </para>
        /// <para>
        /// What an operator should expect is an index rebuild and a brief exclusive lock per table rather than a table
        /// rewrite, over ten tables rather than the seven whose keys move. <c>ALTER TABLE ... ADD PRIMARY KEY</c> builds
        /// the new index and changes no row, and of those seven only <c>email_thread_identifiers</c> is proportional to
        /// the mail corpus — the other six hold roughly one row per account, or per account and folder. The other three
        /// are <c>email_threads</c>, <c>jobs</c>, and <c>mail_folders</c>, whose foreign key onto the account is dropped
        /// and re-added as the pair: a foreign key and a check are each validated by a full scan of the table they are
        /// added to, so each of the three is scanned once and <c>jobs</c> twice for the check beside it. A second of those
        /// grows with the mail corpus — <c>email_threads</c> holds one row per conversation — so a maintenance window is
        /// sized from two corpus-proportional tables rather than one. The whole of it runs in one transaction, so a
        /// deployment that cannot take the lock retries the migration rather than resuming a half-applied one.
        /// </para>
        /// <para>
        /// <c>Down</c> restores the single-column keys and the indexes that led with the identifier alone, which is a
        /// schema a deployment serving one user is still correct under. It refuses to apply where two users have since
        /// declared an account under one identifier, and that refusal is the point: reverting cannot decide which of them
        /// keeps the name.
        /// </para>
        /// </remarks>
        private static class KeyMailAccountByUserAndIdentifier
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_email_threads_mailbox_accounts_MailboxAccountId",
                    table: "email_threads");

                migrationBuilder.DropForeignKey(
                    name: "FK_jobs_mailbox_accounts_MailboxAccountId",
                    table: "jobs");

                migrationBuilder.DropForeignKey(
                    name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                    table: "mail_folders");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_spam_classification_runs",
                    table: "spam_classification_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_mailbox_refresh_tokens",
                    table: "mailbox_refresh_tokens");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_mailbox_accounts",
                    table: "mailbox_accounts");

                migrationBuilder.DropIndex(
                    name: "IX_mailbox_accounts_UserId",
                    table: "mailbox_accounts");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rule_evaluation_runs",
                    table: "mail_rule_evaluation_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rederivation_runs",
                    table: "mail_rederivation_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rederivation_positions",
                    table: "mail_rederivation_positions");

                migrationBuilder.DropIndex(
                    name: "IX_mail_folders_MailboxAccountId",
                    table: "mail_folders");

                migrationBuilder.DropIndex(
                    name: "IX_jobs_MailboxAccountId",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "IX_email_threads_MailboxAccountId",
                    table: "email_threads");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_email_thread_identifiers",
                    table: "email_thread_identifiers");

                migrationBuilder.AddPrimaryKey(
                    name: "pk_spam_classification_runs",
                    table: "spam_classification_runs",
                    columns: new[] { "UserId", "MailboxAccountId" });

                migrationBuilder.AddPrimaryKey(
                    name: "PK_mailbox_refresh_tokens",
                    table: "mailbox_refresh_tokens",
                    columns: new[] { "UserId", "MailboxAccountId" });

                migrationBuilder.AddPrimaryKey(
                    name: "PK_mailbox_accounts",
                    table: "mailbox_accounts",
                    columns: new[] { "UserId", "Id" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rule_evaluation_runs",
                    table: "mail_rule_evaluation_runs",
                    columns: new[] { "UserId", "MailboxAccountId" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rederivation_runs",
                    table: "mail_rederivation_runs",
                    columns: new[] { "UserId", "MailboxAccountId", "FolderAlias" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rederivation_positions",
                    table: "mail_rederivation_positions",
                    columns: new[] { "UserId", "MailboxAccountId", "FolderAlias" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_email_thread_identifiers",
                    table: "email_thread_identifiers",
                    columns: new[] { "UserId", "MailboxAccountId", "IdentifierHash" });

                migrationBuilder.AddCheckConstraint(
                    name: "ck_jobs_account_user",
                    table: "jobs",
                    sql: "(\"UserId\" IS NULL) = (\"MailboxAccountId\" IS NULL)");

                migrationBuilder.CreateIndex(
                    name: "IX_email_threads_UserId_MailboxAccountId",
                    table: "email_threads",
                    columns: new[] { "UserId", "MailboxAccountId" });

                migrationBuilder.AddForeignKey(
                    name: "FK_email_threads_mailbox_accounts_UserId_MailboxAccountId",
                    table: "email_threads",
                    columns: new[] { "UserId", "MailboxAccountId" },
                    principalTable: "mailbox_accounts",
                    principalColumns: new[] { "UserId", "Id" },
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_jobs_mailbox_accounts_UserId_MailboxAccountId",
                    table: "jobs",
                    columns: new[] { "UserId", "MailboxAccountId" },
                    principalTable: "mailbox_accounts",
                    principalColumns: new[] { "UserId", "Id" },
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                    table: "mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId" },
                    principalTable: "mailbox_accounts",
                    principalColumns: new[] { "UserId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_email_threads_mailbox_accounts_UserId_MailboxAccountId",
                    table: "email_threads");

                migrationBuilder.DropForeignKey(
                    name: "FK_jobs_mailbox_accounts_UserId_MailboxAccountId",
                    table: "jobs");

                migrationBuilder.DropForeignKey(
                    name: "FK_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                    table: "mail_folders");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_spam_classification_runs",
                    table: "spam_classification_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_mailbox_refresh_tokens",
                    table: "mailbox_refresh_tokens");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_mailbox_accounts",
                    table: "mailbox_accounts");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rule_evaluation_runs",
                    table: "mail_rule_evaluation_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rederivation_runs",
                    table: "mail_rederivation_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rederivation_positions",
                    table: "mail_rederivation_positions");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_jobs_account_user",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "IX_email_threads_UserId_MailboxAccountId",
                    table: "email_threads");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_email_thread_identifiers",
                    table: "email_thread_identifiers");

                migrationBuilder.AddPrimaryKey(
                    name: "pk_spam_classification_runs",
                    table: "spam_classification_runs",
                    column: "MailboxAccountId");

                migrationBuilder.AddPrimaryKey(
                    name: "PK_mailbox_refresh_tokens",
                    table: "mailbox_refresh_tokens",
                    column: "MailboxAccountId");

                migrationBuilder.AddPrimaryKey(
                    name: "PK_mailbox_accounts",
                    table: "mailbox_accounts",
                    column: "Id");

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rule_evaluation_runs",
                    table: "mail_rule_evaluation_runs",
                    column: "MailboxAccountId");

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rederivation_runs",
                    table: "mail_rederivation_runs",
                    columns: new[] { "MailboxAccountId", "FolderAlias" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rederivation_positions",
                    table: "mail_rederivation_positions",
                    columns: new[] { "MailboxAccountId", "FolderAlias" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_email_thread_identifiers",
                    table: "email_thread_identifiers",
                    columns: new[] { "MailboxAccountId", "IdentifierHash" });

                migrationBuilder.CreateIndex(
                    name: "IX_mailbox_accounts_UserId",
                    table: "mailbox_accounts",
                    column: "UserId");

                migrationBuilder.CreateIndex(
                    name: "IX_mail_folders_MailboxAccountId",
                    table: "mail_folders",
                    column: "MailboxAccountId");

                migrationBuilder.CreateIndex(
                    name: "IX_jobs_MailboxAccountId",
                    table: "jobs",
                    column: "MailboxAccountId");

                migrationBuilder.CreateIndex(
                    name: "IX_email_threads_MailboxAccountId",
                    table: "email_threads",
                    column: "MailboxAccountId");

                migrationBuilder.AddForeignKey(
                    name: "FK_email_threads_mailbox_accounts_MailboxAccountId",
                    table: "email_threads",
                    column: "MailboxAccountId",
                    principalTable: "mailbox_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_jobs_mailbox_accounts_MailboxAccountId",
                    table: "jobs",
                    column: "MailboxAccountId",
                    principalTable: "mailbox_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                    table: "mail_folders",
                    column: "MailboxAccountId",
                    principalTable: "mailbox_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
        }

        /// <summary>The <c>AddUserLabelAndDocumentMarker</c> migration, as it was added.</summary>
        /// <remarks>
        /// The generated shape would have left the label an empty string on every row that already existed, which is not a
        /// label and would have been the value an administrator read the deployment's one user under. The statement
        /// between the generated operations is what makes it apply forward instead: the user this deployment is already
        /// serving is labelled before the index that admits one row per label is created. One label is unique by
        /// construction here, because the only insert this table has ever taken is the single row
        /// <c>AddUserAccounts</c> provisions under <c>WHERE NOT EXISTS</c> — and were that ever untrue, the unique index
        /// would refuse this migration rather than admit two users under one name.
        /// </remarks>
        private static class AddUserLabelAndDocumentMarker
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                // Added nullable and made required below rather than added with a default, for the reason the user column
                // on mailbox_accounts was: a default left on the column would go on supplying a value for a row that named
                // no label, and the label is the one column here nothing may default.
                migrationBuilder.AddColumn<string>(
                    name: "DisplayName",
                    table: "settings_accounts",
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: true);

                // The label the user an upgraded deployment already serves is read under, written while the column still
                // admits the absence and before the index that admits one row per label exists.
                migrationBuilder.Sql(
                    """
                    UPDATE settings_accounts SET "DisplayName" = 'user' WHERE "DisplayName" IS NULL;
                    """);

                migrationBuilder.AlterColumn<string>(
                    name: "DisplayName",
                    table: "settings_accounts",
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(128)",
                    oldMaxLength: 128,
                    oldNullable: true);

                migrationBuilder.AddColumn<bool>(
                    name: "DocumentWrittenAtRuntime",
                    table: "settings_accounts",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);

                migrationBuilder.CreateIndex(
                    name: "ix_settings_accounts_display_name",
                    table: "settings_accounts",
                    column: "DisplayName",
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_settings_accounts_display_name",
                    table: "settings_accounts");

                migrationBuilder.DropColumn(
                    name: "DisplayName",
                    table: "settings_accounts");

                migrationBuilder.DropColumn(
                    name: "DocumentWrittenAtRuntime",
                    table: "settings_accounts");
            }
        }

        /// <summary>The <c>AddUserPasswordCredentials</c> migration, as it was added.</summary>
        private static class AddUserPasswordCredentials
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "user_password_credentials",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                        Enabled = table.Column<bool>(type: "boolean", nullable: false),
                        Version = table.Column<long>(type: "bigint", nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        PasswordChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_user_password_credentials", x => x.Id);
                        table.ForeignKey(
                            name: "FK_user_password_credentials_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_user_password_credentials_user_created_at",
                    table: "user_password_credentials",
                    columns: new[] { "UserId", "CreatedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_user_password_credentials_username",
                    table: "user_password_credentials",
                    column: "Username",
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "user_password_credentials");
            }
        }

        /// <summary>The <c>ResolveUserFacingCredentialsToUserRows</c> migration, as it was added.</summary>
        private static class ResolveUserFacingCredentialsToUserRows
        {
            // Scaffolded as a drop and a create, which would have discarded every credential a user already signs in
            // with, and written out as a rename instead. The table is the one the previous migration created for passwords
            // alone; what widens it is a method column and a grant, so the rows that exist keep working and keep the
            // identifiers an operator wrote down.
            //
            // The method backfill states what the existing rows already were: every row this migration can meet was written
            // by the password credential store, which was the only writer.
            //
            // The grant backfill cannot do the same, and grants nothing rather than guessing. What a password credential
            // was admitted with lived on the endpoint's Authentication entry, which narrowed to the operator's own
            // Permissions list wherever they wrote one — a list this statement cannot see. Backfilling the whole mail
            // surface would therefore widen exactly the deployments that had governed their credentials, handing a
            // read-only sign-in the ability to send mail on an upgrade nobody asked for. So an upgraded credential
            // authenticates and may do nothing until it is provisioned again with the grant its user is meant to have,
            // which is the operator's action this release's changelog names.
            //
            // Neither default survives the statement that adds it, because the model declares none and a column carrying
            // one the model does not know about is a divergence nothing would report.

            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_user_password_credentials_settings_accounts_UserId",
                    table: "user_password_credentials");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_user_password_credentials",
                    table: "user_password_credentials");

                migrationBuilder.DropIndex(
                    name: "ix_user_password_credentials_username",
                    table: "user_password_credentials");

                migrationBuilder.DropIndex(
                    name: "ix_user_password_credentials_user_created_at",
                    table: "user_password_credentials");

                migrationBuilder.RenameTable(
                    name: "user_password_credentials",
                    newName: "user_credentials");

                migrationBuilder.RenameColumn(
                    name: "Username",
                    table: "user_credentials",
                    newName: "Lookup");

                migrationBuilder.RenameColumn(
                    name: "PasswordHash",
                    table: "user_credentials",
                    newName: "Material");

                migrationBuilder.RenameColumn(
                    name: "PasswordChangedAt",
                    table: "user_credentials",
                    newName: "MaterialChangedAt");

                migrationBuilder.AlterColumn<string>(
                    name: "Lookup",
                    table: "user_credentials",
                    type: "character varying(512)",
                    maxLength: 512,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(128)",
                    oldMaxLength: 128);

                migrationBuilder.AlterColumn<string>(
                    name: "Material",
                    table: "user_credentials",
                    type: "character varying(4096)",
                    maxLength: 4096,
                    nullable: true,
                    oldClrType: typeof(string),
                    oldType: "character varying(512)",
                    oldMaxLength: 512);

                migrationBuilder.AddColumn<string>(
                    name: "Method",
                    table: "user_credentials",
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false,
                    defaultValueSql: "'password'");

                migrationBuilder.AddColumn<string[]>(
                    name: "Permissions",
                    table: "user_credentials",
                    type: "text[]",
                    nullable: false,
                    defaultValueSql: "ARRAY[]::text[]");

                migrationBuilder.Sql(
                    """
                    ALTER TABLE user_credentials ALTER COLUMN "Method" DROP DEFAULT;
                    ALTER TABLE user_credentials ALTER COLUMN "Permissions" DROP DEFAULT;
                    """);

                migrationBuilder.AddPrimaryKey(
                    name: "PK_user_credentials",
                    table: "user_credentials",
                    column: "Id");

                migrationBuilder.CreateIndex(
                    name: "ix_user_credentials_method_lookup",
                    table: "user_credentials",
                    columns: new[] { "Method", "Lookup" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_user_credentials_user_created_at",
                    table: "user_credentials",
                    columns: new[] { "UserId", "CreatedAt" });

                migrationBuilder.AddForeignKey(
                    name: "FK_user_credentials_settings_accounts_UserId",
                    table: "user_credentials",
                    column: "UserId",
                    principalTable: "settings_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                // A credential provisioned for one of the three methods the previous schema had no column for cannot be
                // expressed by it, so reversing this drops those rows rather than narrowing them into a shape that would
                // authenticate the wrong way. The password credentials the table started with are kept.
                migrationBuilder.Sql("DELETE FROM user_credentials WHERE \"Method\" <> 'password';");

                migrationBuilder.DropForeignKey(
                    name: "FK_user_credentials_settings_accounts_UserId",
                    table: "user_credentials");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_user_credentials",
                    table: "user_credentials");

                migrationBuilder.DropIndex(
                    name: "ix_user_credentials_method_lookup",
                    table: "user_credentials");

                migrationBuilder.DropIndex(
                    name: "ix_user_credentials_user_created_at",
                    table: "user_credentials");

                migrationBuilder.DropColumn(
                    name: "Method",
                    table: "user_credentials");

                migrationBuilder.DropColumn(
                    name: "Permissions",
                    table: "user_credentials");

                migrationBuilder.RenameTable(
                    name: "user_credentials",
                    newName: "user_password_credentials");

                migrationBuilder.RenameColumn(
                    name: "Lookup",
                    table: "user_password_credentials",
                    newName: "Username");

                migrationBuilder.RenameColumn(
                    name: "Material",
                    table: "user_password_credentials",
                    newName: "PasswordHash");

                migrationBuilder.RenameColumn(
                    name: "MaterialChangedAt",
                    table: "user_password_credentials",
                    newName: "PasswordChangedAt");

                migrationBuilder.AlterColumn<string>(
                    name: "Username",
                    table: "user_password_credentials",
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(512)",
                    oldMaxLength: 512);

                migrationBuilder.AlterColumn<string>(
                    name: "PasswordHash",
                    table: "user_password_credentials",
                    type: "character varying(512)",
                    maxLength: 512,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(4096)",
                    oldMaxLength: 4096,
                    oldNullable: true);

                migrationBuilder.AddPrimaryKey(
                    name: "PK_user_password_credentials",
                    table: "user_password_credentials",
                    column: "Id");

                migrationBuilder.CreateIndex(
                    name: "ix_user_password_credentials_username",
                    table: "user_password_credentials",
                    column: "Username",
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_user_password_credentials_user_created_at",
                    table: "user_password_credentials",
                    columns: new[] { "UserId", "CreatedAt" });

                migrationBuilder.AddForeignKey(
                    name: "FK_user_password_credentials_settings_accounts_UserId",
                    table: "user_password_credentials",
                    column: "UserId",
                    principalTable: "settings_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
        }

        /// <summary>The <c>AddStoredSecrets</c> migration, as it was added.</summary>
        private static class AddStoredSecrets
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "stored_secrets",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        SealedMaterial = table.Column<byte[]>(type: "bytea", nullable: false),
                        DataEncryptionKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_stored_secrets", x => x.Id);
                        table.CheckConstraint("ck_stored_secrets_material_length", "octet_length(\"SealedMaterial\") BETWEEN 29 AND 1048604");
                        table.ForeignKey(
                            name: "fk_stored_secrets_settings_accounts",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_secrets_data_encryption_key",
                    table: "stored_secrets",
                    column: "DataEncryptionKeyId");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_secrets_user_name",
                    table: "stored_secrets",
                    columns: new[] { "UserId", "Name" },
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "stored_secrets");
            }
        }

        /// <summary>The <c>AddDraftSubjectAndStagedAttachments</c> migration, as it was added.</summary>
        private static class AddDraftSubjectAndStagedAttachments
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Subject",
                    table: "mail_drafts",
                    type: "text",
                    nullable: false,
                    defaultValue: "");

                migrationBuilder.CreateTable(
                    name: "mail_draft_attachments",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                        FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                        MediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                        ByteLength = table.Column<long>(type: "bigint", nullable: false),
                        StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_draft_attachments", x => x.Id);
                        table.ForeignKey(
                            name: "fk_mail_draft_attachments_drafts",
                            column: x => x.MailDraftId,
                            principalTable: "mail_drafts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "mail_draft_attachment_contents",
                    columns: table => new
                    {
                        MailDraftAttachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                        Content = table.Column<byte[]>(type: "bytea", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_draft_attachment_contents", x => x.MailDraftAttachmentId);
                        table.ForeignKey(
                            name: "fk_mail_draft_attachment_contents_attachments",
                            column: x => x.MailDraftAttachmentId,
                            principalTable: "mail_draft_attachments",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_draft_attachments_draft_staged",
                    table: "mail_draft_attachments",
                    columns: new[] { "MailDraftId", "StagedAt" });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mail_draft_attachment_contents");

                migrationBuilder.DropTable(
                    name: "mail_draft_attachments");

                migrationBuilder.DropColumn(
                    name: "Subject",
                    table: "mail_drafts");
            }
        }

        /// <summary>The <c>AddClientPreferences</c> migration, as it was added.</summary>
        private static class AddClientPreferences
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "client_preferences",
                    columns: table => new
                    {
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Document = table.Column<string>(type: "jsonb", nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_client_preferences", x => x.UserId);
                        table.ForeignKey(
                            name: "FK_client_preferences_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "client_preferences");
            }
        }

        /// <summary>The <c>AddUserPortrait</c> migration, as it was added.</summary>
        private static class AddUserPortrait
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "user_portraits",
                    columns: table => new
                    {
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Content = table.Column<byte[]>(type: "bytea", nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_user_portraits", x => x.UserId);
                        table.ForeignKey(
                            name: "FK_user_portraits_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "user_portraits");
            }
        }

        /// <summary>The <c>AddNotifications</c> migration, as it was added.</summary>
        private static class AddNotifications
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "notifications",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                        Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                        Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        TargetKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        TargetStoredEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                        TargetScreen = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        DeduplicationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                        OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        IsRead = table.Column<bool>(type: "boolean", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_notifications", x => x.Id);
                        table.ForeignKey(
                            name: "FK_notifications_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "FK_notifications_stored_emails_TargetStoredEmailId",
                            column: x => x.TargetStoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_notifications_user_occurred",
                    table: "notifications",
                    columns: new[] { "UserId", "OccurredAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_notifications_user_unread_condition",
                    table: "notifications",
                    columns: new[] { "UserId", "DeduplicationKey" },
                    unique: true,
                    filter: "NOT \"IsRead\"");

                migrationBuilder.CreateIndex(
                    name: "IX_notifications_TargetStoredEmailId",
                    table: "notifications",
                    column: "TargetStoredEmailId");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "notifications");
            }
        }

        /// <summary>The <c>AddAttachmentTextAndAttachmentChunks</c> migration, as it was added.</summary>
        private static class AddAttachmentTextAndAttachmentChunks
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_email_chunks_email_ordinal",
                    table: "email_chunks");

                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "AttachmentTextDerivedAt",
                    table: "stored_emails",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<int>(
                    name: "AttachmentPosition",
                    table: "email_chunks",
                    type: "integer",
                    nullable: true);

                migrationBuilder.CreateTable(
                    name: "email_attachment_texts",
                    columns: table => new
                    {
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        AttachmentPosition = table.Column<int>(type: "integer", nullable: false),
                        Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        DeclaredMediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                        FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Text = table.Column<string>(type: "text", nullable: true),
                        PageCount = table.Column<int>(type: "integer", nullable: false),
                        Segments = table.Column<string>(type: "jsonb", nullable: true),
                        DerivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        SensitiveContentStamp = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                        SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "CASE WHEN \"Kind\" = 'Document' THEN to_tsvector('simple'::regconfig, coalesce(\"FileName\", '') || ' ' || coalesce(\"Text\", '')) END", stored: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_email_attachment_texts", x => new { x.StoredEmailId, x.AttachmentPosition });
                        table.ForeignKey(
                            name: "FK_email_attachment_texts_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_attachment_text",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "Id" },
                    filter: "\"AttachmentTextDerivedAt\" IS NULL AND \"AttachmentCount\" > 0");

                migrationBuilder.CreateIndex(
                    name: "ix_email_chunks_email",
                    table: "email_chunks",
                    column: "StoredEmailId");

                migrationBuilder.CreateIndex(
                    name: "ix_email_chunks_email_attachment_ordinal",
                    table: "email_chunks",
                    columns: new[] { "StoredEmailId", "AttachmentPosition", "Ordinal" },
                    unique: true,
                    filter: "\"AttachmentPosition\" IS NOT NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_email_chunks_email_ordinal",
                    table: "email_chunks",
                    columns: new[] { "StoredEmailId", "Ordinal" },
                    unique: true,
                    filter: "\"AttachmentPosition\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_email_attachment_texts_search_vector",
                    table: "email_attachment_texts",
                    column: "SearchVector")
                    .Annotation("Npgsql:IndexMethod", "GIN");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "email_attachment_texts");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_attachment_text",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_email_chunks_email",
                    table: "email_chunks");

                migrationBuilder.DropIndex(
                    name: "ix_email_chunks_email_attachment_ordinal",
                    table: "email_chunks");

                migrationBuilder.DropIndex(
                    name: "ix_email_chunks_email_ordinal",
                    table: "email_chunks");

                migrationBuilder.DropColumn(
                    name: "AttachmentTextDerivedAt",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "AttachmentPosition",
                    table: "email_chunks");

                migrationBuilder.CreateIndex(
                    name: "ix_email_chunks_email_ordinal",
                    table: "email_chunks",
                    columns: new[] { "StoredEmailId", "Ordinal" },
                    unique: true);
            }
        }

        /// <summary>The <c>AddEmailEnrichment</c> migration, as it was added.</summary>
        private static class AddEmailEnrichment
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "email_enrichments",
                    columns: table => new
                    {
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        DerivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_email_enrichments", x => x.StoredEmailId);
                        table.ForeignKey(
                            name: "FK_email_enrichments_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "email_enrichment_marks",
                    columns: table => new
                    {
                        Id = table.Column<long>(type: "bigint", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        Aspect = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Text = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                        Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                        DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Origin = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Evidence = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_email_enrichment_marks", x => x.Id);
                        table.ForeignKey(
                            name: "fk_email_enrichment_marks_enrichments",
                            column: x => x.StoredEmailId,
                            principalTable: "email_enrichments",
                            principalColumn: "StoredEmailId",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_email_enrichment_marks_enrichment_aspect",
                    table: "email_enrichment_marks",
                    columns: new[] { "StoredEmailId", "Aspect" },
                    unique: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "email_enrichment_marks");

                migrationBuilder.DropTable(
                    name: "email_enrichments");
            }
        }

        /// <summary>The <c>BoundAndReportAttachmentDerivationSpend</c> migration, as it was added.</summary>
        private static class BoundAndReportAttachmentDerivationSpend
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "attachment_derivation_spend_periods",
                    columns: table => new
                    {
                        PeriodStartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Step = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        ConsumedUnitCount = table.Column<long>(type: "bigint", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_attachment_derivation_spend_periods", x => new { x.PeriodStartsAt, x.UserId, x.Step });
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "attachment_derivation_spend_periods");
            }
        }

        /// <summary>The <c>AddNotificationCause</c> migration, as it was added.</summary>
        private static class AddNotificationCause
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Cause",
                    table: "notifications",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: true);

                migrationBuilder.AddColumn<int>(
                    name: "Counted",
                    table: "notifications",
                    type: "integer",
                    nullable: true);

                migrationBuilder.AddColumn<int>(
                    name: "OutOf",
                    table: "notifications",
                    type: "integer",
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "Cause",
                    table: "notifications");

                migrationBuilder.DropColumn(
                    name: "Counted",
                    table: "notifications");

                migrationBuilder.DropColumn(
                    name: "OutOf",
                    table: "notifications");
            }
        }

        /// <summary>The <c>AddEmailThreadStates</c> migration, as it was added.</summary>
        private static class AddEmailThreadStates
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "email_thread_states",
                    columns: table => new
                    {
                        EmailThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                        Coverage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        DerivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        DerivedFromMessageCount = table.Column<int>(type: "integer", nullable: false),
                        DerivedFromLatestArrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_email_thread_states", x => x.EmailThreadId);
                        table.ForeignKey(
                            name: "FK_email_thread_states_email_threads_EmailThreadId",
                            column: x => x.EmailThreadId,
                            principalTable: "email_threads",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "email_thread_state_entries",
                    columns: table => new
                    {
                        Id = table.Column<long>(type: "bigint", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        EmailThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                        Aspect = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Ordinal = table.Column<int>(type: "integer", nullable: false),
                        Text = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                        OwedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                        DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        Sources = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_email_thread_state_entries", x => x.Id);
                        table.ForeignKey(
                            name: "fk_email_thread_state_entries_states",
                            column: x => x.EmailThreadId,
                            principalTable: "email_thread_states",
                            principalColumn: "EmailThreadId",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_email_thread_state_entries_EmailThreadId_Aspect_Ordinal",
                    table: "email_thread_state_entries",
                    columns: new[] { "EmailThreadId", "Aspect", "Ordinal" });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "email_thread_state_entries");

                migrationBuilder.DropTable(
                    name: "email_thread_states");
            }
        }

        /// <summary>The <c>WidenFilingJoinIndexesToEveryConfirmedCopy</c> migration, as it was added.</summary>
        private static class WidenFilingJoinIndexesToEveryConfirmedCopy
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                    filter: "\"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"Stage\" = 'Confirmed'");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                    filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'");
            }
        }

        /// <summary>The <c>IndexStandingFilingsByFilingAndAppend</c> migration, as it was added.</summary>
        private static class IndexStandingFilingsByFilingAndAppend
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_recent_by_filing",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "Filing", "AppendedAt", "OutgoingEmailId" },
                    filter: "\"Stage\" = 'Confirmed'");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_recent_by_filing",
                    table: "outgoing_email_filings");
            }
        }

        /// <summary>The <c>AddWorkLeases</c> migration, as it was added.</summary>
        private static class AddWorkLeases
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "work_leases",
                    columns: table => new
                    {
                        Scope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                        Holder = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        HeldSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_work_leases", x => x.Scope);
                    });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "work_leases");
            }
        }

        /// <summary>The <c>AddSpentClientAssertions</c> migration, as it was added.</summary>
        private static class AddSpentClientAssertions
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "spent_client_assertions",
                    columns: table => new
                    {
                        CredentialKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_spent_client_assertions", x => new { x.CredentialKey, x.Identifier });
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_spent_client_assertions_expires_at",
                    table: "spent_client_assertions",
                    column: "ExpiresAt");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "spent_client_assertions");
            }
        }

        /// <summary>The <c>AddDeploymentWideCeilingRows</c> migration, as it was added.</summary>
        private static class AddDeploymentWideCeilingRows
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "mail_answering_spend_periods",
                    columns: table => new
                    {
                        PeriodStartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        AdmittedRunCount = table.Column<int>(type: "integer", nullable: false),
                        ConsumedTokenCount = table.Column<long>(type: "bigint", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_answering_spend_periods", x => x.PeriodStartsAt);
                    });

                migrationBuilder.CreateTable(
                    name: "provider_pace_markers",
                    columns: table => new
                    {
                        Workload = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        NextSlotAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_provider_pace_markers", x => x.Workload);
                    });

                migrationBuilder.CreateTable(
                    name: "stored_content_claims",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        ClaimedByteCount = table.Column<long>(type: "bigint", nullable: false),
                        ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_stored_content_claims", x => x.Id);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_stored_content_claims_ExpiresAt",
                    table: "stored_content_claims",
                    column: "ExpiresAt");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mail_answering_spend_periods");

                migrationBuilder.DropTable(
                    name: "provider_pace_markers");

                migrationBuilder.DropTable(
                    name: "stored_content_claims");
            }
        }

        /// <summary>The <c>AddMailboxMutationWithdrawalHold</c> migration, as it was added.</summary>
        private static class AddMailboxMutationWithdrawalHold
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "HeldUntil",
                    table: "mailbox_mutations",
                    type: "timestamp with time zone",
                    nullable: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "HeldUntil",
                    table: "mailbox_mutations");
            }
        }

        /// <summary>The <c>DropUserDocumentMarker</c> migration, as it was added.</summary>
        private static class DropUserDocumentMarker
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "DocumentWrittenAtRuntime",
                    table: "settings_accounts");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<bool>(
                    name: "DocumentWrittenAtRuntime",
                    table: "settings_accounts",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);
            }
        }

        /// <summary>The <c>AddClientSignalTickets</c> migration, as it was added.</summary>
        private static class AddClientSignalTickets
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "client_signal_tickets",
                    columns: table => new
                    {
                        Identifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        SecretDigest = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                        ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_client_signal_tickets", x => x.Identifier);
                        table.ForeignKey(
                            name: "FK_client_signal_tickets_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_client_signal_tickets_expires_at",
                    table: "client_signal_tickets",
                    column: "ExpiresAt");

                migrationBuilder.CreateIndex(
                    name: "IX_client_signal_tickets_UserId",
                    table: "client_signal_tickets",
                    column: "UserId");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "client_signal_tickets");
            }
        }

        /// <summary>The <c>AddWorkLeaseReplica</c> migration, as it was added.</summary>
        private static class AddWorkLeaseReplica
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Replica",
                    table: "work_leases",
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false,
                    defaultValue: "unknown");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "Replica",
                    table: "work_leases");
            }
        }

        /// <summary>The <c>AddClientSessions</c> migration, as it was added.</summary>
        private static class AddClientSessions
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "client_sessions",
                    columns: table => new
                    {
                        Identifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        CredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                        Permissions = table.Column<string[]>(type: "text[]", nullable: false),
                        SecretDigest = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                        ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_client_sessions", x => x.Identifier);
                        table.ForeignKey(
                            name: "FK_client_sessions_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "FK_client_sessions_user_credentials_CredentialId",
                            column: x => x.CredentialId,
                            principalTable: "user_credentials",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_client_sessions_CredentialId",
                    table: "client_sessions",
                    column: "CredentialId");

                migrationBuilder.CreateIndex(
                    name: "ix_client_sessions_expires_at",
                    table: "client_sessions",
                    column: "ExpiresAt");

                migrationBuilder.CreateIndex(
                    name: "IX_client_sessions_UserId",
                    table: "client_sessions",
                    column: "UserId");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "client_sessions");
            }
        }

        /// <summary>The <c>MakeStoredEmailOccurrenceOptional</c> migration, as it was added.</summary>
        private static class MakeStoredEmailOccurrenceOptional
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterColumn<long>(
                    name: "UidValidity",
                    table: "stored_emails",
                    type: "bigint",
                    nullable: true,
                    oldClrType: typeof(long),
                    oldType: "bigint");

                migrationBuilder.AlterColumn<long>(
                    name: "Uid",
                    table: "stored_emails",
                    type: "bigint",
                    nullable: true,
                    oldClrType: typeof(long),
                    oldType: "bigint");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_stored_emails_occurrence_complete",
                    table: "stored_emails",
                    sql: "(\"UidValidity\" IS NULL) = (\"Uid\" IS NULL)");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                // Restoring NOT NULL fails on a row that carries no occurrence, deliberately: such a row is a message no
                // server holds, and no occurrence could be written back for it.
                migrationBuilder.DropCheckConstraint(
                    name: "ck_stored_emails_occurrence_complete",
                    table: "stored_emails");

                migrationBuilder.AlterColumn<long>(
                    name: "UidValidity",
                    table: "stored_emails",
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L,
                    oldClrType: typeof(long),
                    oldType: "bigint",
                    oldNullable: true);

                migrationBuilder.AlterColumn<long>(
                    name: "Uid",
                    table: "stored_emails",
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L,
                    oldClrType: typeof(long),
                    oldType: "bigint",
                    oldNullable: true);
            }
        }

        /// <summary>The <c>AddUserEndpointSwitches</c> migration, as it was added.</summary>
        private static class AddUserEndpointSwitches
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<bool>(
                    name: "ClientEndpointEnabled",
                    table: "settings_accounts",
                    type: "boolean",
                    nullable: false,
                    defaultValue: true);

                migrationBuilder.AddColumn<bool>(
                    name: "McpEndpointEnabled",
                    table: "settings_accounts",
                    type: "boolean",
                    nullable: false,
                    defaultValue: true);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "ClientEndpointEnabled",
                    table: "settings_accounts");

                migrationBuilder.DropColumn(
                    name: "McpEndpointEnabled",
                    table: "settings_accounts");
            }
        }

        /// <summary>The <c>MoveUserPortraitsIntoStoredFiles</c> migration, as it was added.</summary>
        private static class MoveUserPortraitsIntoStoredFiles
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "stored_files",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        MediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                        ByteLength = table.Column<long>(type: "bigint", nullable: false),
                        Sha256Hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                        Backend = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "Database"),
                        Content = table.Column<byte[]>(type: "bytea", nullable: true),
                        ObjectLocator = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                        ObjectVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_stored_files", x => x.Id);
                        table.CheckConstraint("ck_stored_files_backend_payload", "(\"Backend\" = 'Database' AND \"Content\" IS NOT NULL AND \"ObjectLocator\" IS NULL AND \"ObjectVerifiedAt\" IS NULL)\nOR (\"Backend\" = 'ObjectStorage' AND \"ObjectLocator\" IS NOT NULL\n    AND (\"Content\" IS NULL OR \"ObjectVerifiedAt\" IS NOT NULL))");
                        table.ForeignKey(
                            name: "fk_stored_files_settings_accounts",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_files_object_locator",
                    table: "stored_files",
                    column: "ObjectLocator",
                    unique: true,
                    filter: "\"Backend\" = 'ObjectStorage'");

                migrationBuilder.CreateIndex(
                    name: "IX_stored_files_UserId",
                    table: "stored_files",
                    column: "UserId");

                // Every portrait is carried into the database backend, whatever ContentStorage selects: the move is what
                // takes a payload to the object backend, and it reaches stored files like any other kind. The kind was
                // proven from the signature when the row was written, so the first three octets decide the media type. The
                // link is written into the record and its version advanced, so a buffer opened over the old record is
                // refused rather than saved over the link.
                migrationBuilder.Sql(
                    """
                    WITH carried AS (
                        INSERT INTO stored_files ("Id", "UserId", "MediaType", "ByteLength", "Sha256Hash", "Backend", "Content", "CreatedAt")
                        SELECT gen_random_uuid(),
                               p."UserId",
                               CASE WHEN substring(p."Content" FROM 1 FOR 3) = '\xFFD8FF'::bytea THEN 'image/jpeg' ELSE 'image/png' END,
                               length(p."Content"),
                               sha256(p."Content"),
                               'Database',
                               p."Content",
                               p."UpdatedAt"
                        FROM user_portraits AS p
                        RETURNING "Id", "UserId"
                    )
                    UPDATE settings_accounts AS u
                    SET "Document" = jsonb_set(u."Document", '{Portrait}', to_jsonb(c."Id"::text)),
                        "Version" = u."Version" + 1
                    FROM carried AS c
                    WHERE u."Id" = c."UserId";
                    """);

                migrationBuilder.DropTable(
                    name: "user_portraits");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "user_portraits",
                    columns: table => new
                    {
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Content = table.Column<byte[]>(type: "bytea", nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_user_portraits", x => x.UserId);
                        table.ForeignKey(
                            name: "FK_user_portraits_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                // Only a portrait whose octets the database still holds can be carried back; one already moved into the
                // object backend has nowhere in the older schema to be read from, and is dropped with its link.
                migrationBuilder.Sql(
                    """
                    INSERT INTO user_portraits ("UserId", "Content", "CreatedAt", "UpdatedAt")
                    SELECT f."UserId", f."Content", f."CreatedAt", f."CreatedAt"
                    FROM stored_files AS f
                    JOIN settings_accounts AS u ON u."Id" = f."UserId"
                    WHERE f."Content" IS NOT NULL
                      AND lower(u."Document" ->> 'Portrait') = f."Id"::text;

                    UPDATE settings_accounts
                    SET "Document" = "Document" - 'Portrait',
                        "Version" = "Version" + 1
                    WHERE "Document" ? 'Portrait';
                    """);

                migrationBuilder.DropTable(
                    name: "stored_files");
            }
        }

        /// <summary>The <c>AddLocalMailFolders</c> migration, as it was added.</summary>
        private static class AddLocalMailFolders
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "LocalMailFolderId",
                    table: "stored_emails",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "CustodyPhase",
                    table: "mailbox_accounts",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValueSql: "'Mirrored'");

                migrationBuilder.AddColumn<int>(
                    name: "LocalMailFoldersRevision",
                    table: "mailbox_accounts",
                    type: "integer",
                    nullable: false,
                    defaultValue: 0);

                migrationBuilder.CreateTable(
                    name: "local_mail_folders",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                        Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                        NameKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                        Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                        SourceFolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                        ErasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_local_mail_folders", x => x.Id);
                        table.ForeignKey(
                            name: "FK_local_mail_folders_local_mail_folders_ParentId",
                            column: x => x.ParentId,
                            principalTable: "local_mail_folders",
                            principalColumn: "Id");
                        table.ForeignKey(
                            name: "FK_local_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                            columns: x => new { x.UserId, x.MailboxAccountId },
                            principalTable: "mailbox_accounts",
                            principalColumns: new[] { "UserId", "Id" },
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_stored_emails_LocalMailFolderId",
                    table: "stored_emails",
                    column: "LocalMailFolderId",
                    filter: "\"LocalMailFolderId\" IS NOT NULL");

                migrationBuilder.CreateIndex(
                    name: "IX_local_mail_folders_ParentId",
                    table: "local_mail_folders",
                    column: "ParentId");

                migrationBuilder.CreateIndex(
                    name: "ix_local_mail_folders_user_account_parent_name",
                    table: "local_mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId", "ParentId", "NameKey" },
                    unique: true,
                    filter: "\"ErasedAt\" IS NULL")
                    .Annotation("Npgsql:NullsDistinct", false);

                migrationBuilder.CreateIndex(
                    name: "ix_local_mail_folders_user_account_role",
                    table: "local_mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId", "Role" },
                    unique: true,
                    filter: "\"Role\" IS NOT NULL AND \"ErasedAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "IX_local_mail_folders_UserId_MailboxAccountId_ErasedAt",
                    table: "local_mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId", "ErasedAt" });

                migrationBuilder.AddForeignKey(
                    name: "FK_stored_emails_local_mail_folders_LocalMailFolderId",
                    table: "stored_emails",
                    column: "LocalMailFolderId",
                    principalTable: "local_mail_folders",
                    principalColumn: "Id");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_stored_emails_local_mail_folders_LocalMailFolderId",
                    table: "stored_emails");

                migrationBuilder.DropTable(
                    name: "local_mail_folders");

                migrationBuilder.DropIndex(
                    name: "IX_stored_emails_LocalMailFolderId",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "LocalMailFolderId",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "CustodyPhase",
                    table: "mailbox_accounts");

                migrationBuilder.DropColumn(
                    name: "LocalMailFoldersRevision",
                    table: "mailbox_accounts");
            }
        }

        /// <summary>The <c>AddOrganizationsScopingPasswordLogins</c> migration, as it was added.</summary>
        private static class AddOrganizationsScopingPasswordLogins
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_user_credentials_method_lookup",
                    table: "user_credentials");

                migrationBuilder.AddColumn<Guid>(
                    name: "OrganizationId",
                    table: "user_credentials",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "OrganizationId",
                    table: "settings_accounts",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.CreateTable(
                    name: "organizations",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        ShortName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_organizations", x => x.Id);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_user_credentials_method_organization_lookup",
                    table: "user_credentials",
                    columns: new[] { "Method", "OrganizationId", "Lookup" },
                    unique: true)
                    .Annotation("Npgsql:NullsDistinct", false);

                migrationBuilder.CreateIndex(
                    name: "IX_user_credentials_OrganizationId",
                    table: "user_credentials",
                    column: "OrganizationId");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_user_credentials_organization_scopes_password",
                    table: "user_credentials",
                    sql: "\"OrganizationId\" IS NULL OR \"Method\" = 'password'");

                migrationBuilder.CreateIndex(
                    name: "ix_settings_accounts_organization",
                    table: "settings_accounts",
                    column: "OrganizationId");

                migrationBuilder.CreateIndex(
                    name: "ix_organizations_short_name",
                    table: "organizations",
                    column: "ShortName",
                    unique: true);

                migrationBuilder.AddForeignKey(
                    name: "FK_settings_accounts_organizations_OrganizationId",
                    table: "settings_accounts",
                    column: "OrganizationId",
                    principalTable: "organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);

                migrationBuilder.AddForeignKey(
                    name: "FK_user_credentials_organizations_OrganizationId",
                    table: "user_credentials",
                    column: "OrganizationId",
                    principalTable: "organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_settings_accounts_organizations_OrganizationId",
                    table: "settings_accounts");

                migrationBuilder.DropForeignKey(
                    name: "FK_user_credentials_organizations_OrganizationId",
                    table: "user_credentials");

                migrationBuilder.DropTable(
                    name: "organizations");

                migrationBuilder.DropIndex(
                    name: "ix_user_credentials_method_organization_lookup",
                    table: "user_credentials");

                migrationBuilder.DropIndex(
                    name: "IX_user_credentials_OrganizationId",
                    table: "user_credentials");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_user_credentials_organization_scopes_password",
                    table: "user_credentials");

                migrationBuilder.DropIndex(
                    name: "ix_settings_accounts_organization",
                    table: "settings_accounts");

                migrationBuilder.DropColumn(
                    name: "OrganizationId",
                    table: "user_credentials");

                migrationBuilder.DropColumn(
                    name: "OrganizationId",
                    table: "settings_accounts");

                migrationBuilder.CreateIndex(
                    name: "ix_user_credentials_method_lookup",
                    table: "user_credentials",
                    columns: new[] { "Method", "Lookup" },
                    unique: true);
            }
        }

        /// <summary>The <c>FileHeldAccountDraftsAndSentCopiesLocally</c> migration, as it was added.</summary>
        private static class FileHeldAccountDraftsAndSentCopiesLocally
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<int>(
                    name: "FiledRevision",
                    table: "mail_drafts",
                    type: "integer",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "FiledStoredEmailId",
                    table: "mail_drafts",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_filed_sent_copy",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                    filter: "\"FiledFromOutgoingEmailId\" IS NOT NULL AND \"UidValidity\" IS NULL");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_filed_sent_copy",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "FiledRevision",
                    table: "mail_drafts");

                migrationBuilder.DropColumn(
                    name: "FiledStoredEmailId",
                    table: "mail_drafts");
            }
        }

        /// <summary>The <c>MoveMailAccountsIntoRecordsOfTheirOwn</c> migration, as it was added.</summary>
        private static class MoveMailAccountsIntoRecordsOfTheirOwn
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "settings_mail_accounts",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        EmailAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                        NormalizedEmailAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                        DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        Document = table.Column<string>(type: "jsonb", nullable: false),
                        Version = table.Column<long>(type: "bigint", nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_settings_mail_accounts", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "mail_account_assignments",
                    columns: table => new
                    {
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        MailAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                        AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_account_assignments", x => new { x.UserId, x.MailAccountId });
                        table.ForeignKey(
                            name: "fk_mail_account_assignments_settings_accounts",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "fk_mail_account_assignments_settings_mail_accounts",
                            column: x => x.MailAccountId,
                            principalTable: "settings_mail_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_account_assignments_mail_account_id",
                    table: "mail_account_assignments",
                    column: "MailAccountId",
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_settings_mail_accounts_normalized_email_address",
                    table: "settings_mail_accounts",
                    column: "NormalizedEmailAddress",
                    unique: true,
                    filter: "\"NormalizedEmailAddress\" IS NOT NULL");

                // Stored mail is keyed by the operator-chosen identifier this migration retires, so it is discarded rather
                // than rewritten and every account synchronizes again under its record's identifier. The statements are the
                // per-account erasure's own set: the tables no cascade from mailbox_accounts reaches, then mailbox_accounts
                // itself. A refresh token is sealed with the old identifier as associated data, so it cannot be carried and
                // an OAuth mailbox is consented again. A schedule for a rule names the account inside its key.
                // Payloads already moved to object storage are not released here; a content move reclaims them.
                migrationBuilder.Sql(
                    """
                    DELETE FROM mail_answering_audit_entries;
                    DELETE FROM mail_drafts;
                    DELETE FROM mail_rederivation_positions;
                    DELETE FROM mail_rederivation_runs;
                    DELETE FROM mail_rule_evaluation_runs;
                    DELETE FROM mailbox_mutation_audit_entries;
                    DELETE FROM mailbox_refresh_tokens;
                    DELETE FROM outgoing_emails;
                    DELETE FROM recurring_sends;
                    DELETE FROM spam_classification_runs;
                    DELETE FROM jobs WHERE "MailboxAccountId" IS NOT NULL;
                    DELETE FROM mailbox_accounts;
                    DELETE FROM job_schedules WHERE "ScheduleId" LIKE 'mail-rules:%';
                    """);

                // Each account a user's record declared becomes a record of its own assigned to that user. The address is
                // the login where the login is one and the delivery sender otherwise; one that is not a single address, or
                // that an earlier account already claimed, is left unset for an administrator to state, because the index
                // allows no second holder. Both collection shapes a record can carry — an array, and an object keyed by
                // position — are read in order.
                migrationBuilder.Sql(
                    """
                    WITH declared AS (
                        SELECT u."Id" AS user_id, entry.account, entry.position
                        FROM settings_accounts AS u
                        CROSS JOIN LATERAL (
                            SELECT value AS account, ordinality AS position
                            FROM jsonb_array_elements(
                                CASE jsonb_typeof(u."Document" -> 'MailAccounts')
                                    WHEN 'array' THEN u."Document" -> 'MailAccounts'
                                    WHEN 'object' THEN (
                                        SELECT coalesce(jsonb_agg(e.value ORDER BY CASE WHEN e.key ~ '^[0-9]{1,9}$' THEN e.key::int END, e.key), '[]'::jsonb)
                                        FROM jsonb_each(u."Document" -> 'MailAccounts') AS e)
                                    ELSE '[]'::jsonb
                                END) WITH ORDINALITY
                        ) AS entry
                        WHERE jsonb_typeof(entry.account) = 'object'
                    ),
                    addressed AS (
                        SELECT gen_random_uuid() AS id, user_id, account, position,
                               nullif(btrim(CASE WHEN account ->> 'UserName' LIKE '%@%' THEN account ->> 'UserName'
                                                 ELSE account #>> '{Delivery,FromAddress}' END), '') AS address
                        FROM declared
                    ),
                    ranked AS (
                        SELECT id, user_id, account,
                               CASE WHEN address ~ '^[^@[:space:]]+@[^@[:space:]]+$' AND length(address) <= 320
                                         AND row_number() OVER (PARTITION BY upper(address) ORDER BY user_id, position) = 1
                                    THEN address END AS address
                        FROM addressed
                    ),
                    recorded AS (
                        INSERT INTO settings_mail_accounts
                            ("Id", "EmailAddress", "NormalizedEmailAddress", "DisplayName", "Document", "Version", "CreatedAt", "UpdatedAt")
                        SELECT id, address, upper(address),
                               left(coalesce(nullif(btrim(account ->> 'DisplayName'), ''), nullif(btrim(account ->> 'AccountId'), ''), 'Mail account'), 128),
                               account - 'AccountId' - 'DisplayName' - 'EmailAddress', 1, now(), now()
                        FROM ranked
                        RETURNING "Id"
                    )
                    INSERT INTO mail_account_assignments ("UserId", "MailAccountId", "AssignedAt")
                    SELECT r.user_id, r.id, now()
                    FROM ranked AS r
                    JOIN recorded ON recorded."Id" = r.id;

                    UPDATE settings_accounts
                    SET "Document" = "Document" - 'MailAccounts',
                        "Version" = "Version" + 1
                    WHERE "Document" ? 'MailAccounts';
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                // Each assignment carries its account back into that user's record under the record's identifier, so an
                // account shared by several users is declared in each of their records. Mail discarded on the way up stays
                // discarded.
                migrationBuilder.Sql(
                    """
                    UPDATE settings_accounts AS u
                    SET "Document" = jsonb_set(u."Document", '{MailAccounts}', carried.accounts),
                        "Version" = u."Version" + 1
                    FROM (
                        SELECT a."UserId",
                               jsonb_agg(m."Document" || jsonb_build_object('AccountId', m."Id"::text, 'DisplayName', m."DisplayName")
                                         ORDER BY a."AssignedAt", m."Id") AS accounts
                        FROM mail_account_assignments AS a
                        JOIN settings_mail_accounts AS m ON m."Id" = a."MailAccountId"
                        GROUP BY a."UserId"
                    ) AS carried
                    WHERE u."Id" = carried."UserId";
                    """);

                migrationBuilder.DropTable(
                    name: "mail_account_assignments");

                migrationBuilder.DropTable(
                    name: "settings_mail_accounts");
            }
        }

        /// <summary>The <c>HoldSpamAndSensitiveContentOnTheMailAccount</c> migration, as it was added.</summary>
        /// <remarks>
        /// SQL only and no model change, deliberately: both blocks live inside a <c>jsonb</c> document that the schema
        /// declares as one column, so moving them between two documents changes what those columns hold and nothing EF
        /// maps. What it does change is which settings the binder accepts on which record, which is why the rows have to
        /// move in the same release: a user record still carrying either block is refused as a property nothing binds, and
        /// an account carrying neither is classified and scanned as though its user had asked for nothing.
        /// </remarks>
        private static class HoldSpamAndSensitiveContentOnTheMailAccount
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                // Each user's two blocks are carried into every account assigned to them and then removed from the record.
                // The account's own value wins where it already carries one, which is what the concatenation order says.
                // An account is assigned to one user — the assignment index is unique on the account — so no account is
                // written from two records and the join can only match once.
                migrationBuilder.Sql(
                    """
                    WITH carried AS (
                        SELECT a."MailAccountId" AS account_id,
                               (CASE WHEN u."Document" ? 'SpamClassification'
                                     THEN jsonb_build_object('SpamClassification', u."Document" -> 'SpamClassification')
                                     ELSE '{}'::jsonb END)
                               || (CASE WHEN u."Document" ? 'SensitiveContent'
                                        THEN jsonb_build_object('SensitiveContent', u."Document" -> 'SensitiveContent')
                                        ELSE '{}'::jsonb END) AS blocks
                        FROM mail_account_assignments AS a
                        JOIN settings_accounts AS u ON u."Id" = a."UserId"
                        WHERE u."Document" ? 'SpamClassification' OR u."Document" ? 'SensitiveContent'
                    )
                    UPDATE settings_mail_accounts AS m
                    SET "Document" = carried.blocks || m."Document",
                        "Version" = m."Version" + 1,
                        "UpdatedAt" = now()
                    FROM carried
                    WHERE m."Id" = carried.account_id;

                    UPDATE settings_accounts
                    SET "Document" = "Document" - 'SpamClassification' - 'SensitiveContent',
                        "Version" = "Version" + 1
                    WHERE "Document" ? 'SpamClassification' OR "Document" ? 'SensitiveContent';
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                // The blocks go back onto the user record, from the first account assigned to them that carries one: a
                // user record holds one answer for all their mail, so a second account stating something else has nowhere
                // to be written back to and its settings are dropped rather than composed into an answer nobody chose.
                migrationBuilder.Sql(
                    """
                    WITH carried AS (
                        SELECT a."UserId" AS user_id,
                               (CASE WHEN m."Document" ? 'SpamClassification'
                                     THEN jsonb_build_object('SpamClassification', m."Document" -> 'SpamClassification')
                                     ELSE '{}'::jsonb END)
                               || (CASE WHEN m."Document" ? 'SensitiveContent'
                                        THEN jsonb_build_object('SensitiveContent', m."Document" -> 'SensitiveContent')
                                        ELSE '{}'::jsonb END) AS blocks,
                               row_number() OVER (PARTITION BY a."UserId" ORDER BY a."AssignedAt", m."Id") AS carrier
                        FROM mail_account_assignments AS a
                        JOIN settings_mail_accounts AS m ON m."Id" = a."MailAccountId"
                        WHERE m."Document" ? 'SpamClassification' OR m."Document" ? 'SensitiveContent'
                    )
                    UPDATE settings_accounts AS u
                    SET "Document" = u."Document" || carried.blocks,
                        "Version" = u."Version" + 1
                    FROM carried
                    WHERE u."Id" = carried.user_id AND carried.carrier = 1;

                    UPDATE settings_mail_accounts
                    SET "Document" = "Document" - 'SpamClassification' - 'SensitiveContent',
                        "Version" = "Version" + 1,
                        "UpdatedAt" = now()
                    WHERE "Document" ? 'SpamClassification' OR "Document" ? 'SensitiveContent';
                    """);
            }
        }

        /// <summary>The <c>KeyTheMailGraphByTheAccountIdentifier</c> migration, as it was added.</summary>
        private static class KeyTheMailGraphByTheAccountIdentifier
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_email_threads_mailbox_accounts_UserId_MailboxAccountId",
                    table: "email_threads");

                migrationBuilder.DropForeignKey(
                    name: "FK_jobs_mailbox_accounts_UserId_MailboxAccountId",
                    table: "jobs");

                migrationBuilder.DropForeignKey(
                    name: "FK_local_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                    table: "local_mail_folders");

                migrationBuilder.DropForeignKey(
                    name: "FK_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                    table: "mail_folders");

                migrationBuilder.DropForeignKey(
                    name: "FK_mailbox_accounts_settings_accounts_UserId",
                    table: "mailbox_accounts");

                migrationBuilder.DropTable(
                    name: "user_stored_content");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_attachment_text",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_filed_sent_copy",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_user_account_identity",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_user_account_timeline",
                    table: "stored_emails");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_spam_classification_runs",
                    table: "spam_classification_runs");

                migrationBuilder.DropIndex(
                    name: "ix_recurring_sends_identity",
                    table: "recurring_sends");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_identity",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_outstanding",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_recent_by_filing",
                    table: "outgoing_email_filings");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_mailbox_refresh_tokens",
                    table: "mailbox_refresh_tokens");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_outstanding",
                    table: "mailbox_mutations");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutation_audit_entries_user_account_completed",
                    table: "mailbox_mutation_audit_entries");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_mailbox_accounts",
                    table: "mailbox_accounts");

                migrationBuilder.DropIndex(
                    name: "ix_mail_rule_executions_user_account_evaluated",
                    table: "mail_rule_executions");

                migrationBuilder.DropIndex(
                    name: "ix_mail_rule_executions_user_account_rule_evaluated",
                    table: "mail_rule_executions");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rule_evaluation_runs",
                    table: "mail_rule_evaluation_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rederivation_runs",
                    table: "mail_rederivation_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rederivation_positions",
                    table: "mail_rederivation_positions");

                migrationBuilder.DropIndex(
                    name: "ix_mail_folders_user_account_alias_generation",
                    table: "mail_folders");

                migrationBuilder.DropIndex(
                    name: "ix_mail_answering_audit_entries_run_user_account",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropIndex(
                    name: "ix_mail_answering_audit_entries_user_account_completed",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropIndex(
                    name: "ix_mail_account_assignments_mail_account_id",
                    table: "mail_account_assignments");

                migrationBuilder.DropIndex(
                    name: "ix_local_mail_folders_user_account_parent_name",
                    table: "local_mail_folders");

                migrationBuilder.DropIndex(
                    name: "ix_local_mail_folders_user_account_role",
                    table: "local_mail_folders");

                migrationBuilder.DropIndex(
                    name: "IX_local_mail_folders_UserId_MailboxAccountId_ErasedAt",
                    table: "local_mail_folders");

                migrationBuilder.DropIndex(
                    name: "ix_jobs_user_account",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "ix_jobs_user_turn",
                    table: "jobs");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_jobs_account_user",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "IX_email_threads_UserId_MailboxAccountId",
                    table: "email_threads");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_email_thread_identifiers",
                    table: "email_thread_identifiers");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "stored_content_claims");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "spam_classification_runs");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "outgoing_email_filings");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mailbox_refresh_tokens");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mailbox_mutations");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mailbox_mutation_audit_entries");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mailbox_accounts");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_rule_executions");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_rule_evaluation_runs");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_rederivation_runs");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_rederivation_positions");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_folders");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "local_mail_folders");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "jobs");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "email_threads");

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "email_thread_identifiers");

                migrationBuilder.AddColumn<string>(
                    name: "MailboxAccountId",
                    table: "stored_content_claims",
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false,
                    defaultValue: "");

                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: "outgoing_emails",
                    type: "uuid",
                    nullable: true,
                    oldClrType: typeof(Guid),
                    oldType: "uuid");

                migrationBuilder.AddPrimaryKey(
                    name: "pk_spam_classification_runs",
                    table: "spam_classification_runs",
                    column: "MailboxAccountId");

                migrationBuilder.AddPrimaryKey(
                    name: "PK_mailbox_refresh_tokens",
                    table: "mailbox_refresh_tokens",
                    column: "MailboxAccountId");

                migrationBuilder.AddPrimaryKey(
                    name: "PK_mailbox_accounts",
                    table: "mailbox_accounts",
                    column: "Id");

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rule_evaluation_runs",
                    table: "mail_rule_evaluation_runs",
                    column: "MailboxAccountId");

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rederivation_runs",
                    table: "mail_rederivation_runs",
                    columns: new[] { "MailboxAccountId", "FolderAlias" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rederivation_positions",
                    table: "mail_rederivation_positions",
                    columns: new[] { "MailboxAccountId", "FolderAlias" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_email_thread_identifiers",
                    table: "email_thread_identifiers",
                    columns: new[] { "MailboxAccountId", "IdentifierHash" });

                migrationBuilder.CreateTable(
                    name: "account_stored_content",
                    columns: table => new
                    {
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        StoredContentByteCount = table.Column<long>(type: "bigint", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_account_stored_content", x => x.MailboxAccountId);
                        table.ForeignKey(
                            name: "FK_account_stored_content_mailbox_accounts_MailboxAccountId",
                            column: x => x.MailboxAccountId,
                            principalTable: "mailbox_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_account_identity",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_account_timeline",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "ReceivedAt", "Id" },
                    descending: new[] { false, true, true })
                    .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_attachment_text",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "Id" },
                    filter: "\"AttachmentTextDerivedAt\" IS NULL AND \"AttachmentCount\" > 0");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "Id" },
                    filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_filed_sent_copy",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "InternetMessageId" },
                    filter: "\"FiledFromOutgoingEmailId\" IS NOT NULL AND \"UidValidity\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_recurring_sends_identity",
                    table: "recurring_sends",
                    columns: new[] { "MailboxAccountId", "UserId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails",
                    columns: new[] { "MailboxAccountId", "AvailableAt", "Id" },
                    filter: "\"Stage\" = 'Recorded'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_identity",
                    table: "outgoing_emails",
                    columns: new[] { "MailboxAccountId", "UserId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true)
                    .Annotation("Npgsql:NullsDistinct", false);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_outstanding",
                    table: "outgoing_emails",
                    columns: new[] { "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" NOT IN ('Sent', 'Refused', 'Cancelled')");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails",
                    columns: new[] { "RecordedAt", "MailboxAccountId" });

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings",
                    columns: new[] { "MailboxAccountId", "InternetMessageId" },
                    filter: "\"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings",
                    columns: new[] { "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_recent_by_filing",
                    table: "outgoing_email_filings",
                    columns: new[] { "MailboxAccountId", "Filing", "AppendedAt", "OutgoingEmailId" },
                    filter: "\"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_outstanding",
                    table: "mailbox_mutations",
                    columns: new[] { "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" <> 'Completed'");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations",
                    columns: new[] { "MailboxAccountId", "DestinationFolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"PlacementObservedAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutation_audit_entries_account_completed",
                    table: "mailbox_mutation_audit_entries",
                    columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_account_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "MailboxAccountId", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_account_rule_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "MailboxAccountId", "RuleName", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_folders_account_alias_generation",
                    table: "mail_folders",
                    columns: new[] { "MailboxAccountId", "Alias", "ResolutionGeneration" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_account_completed",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_run_account",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "RunId", "MailboxAccountId" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mail_account_assignments_mail_account_id",
                    table: "mail_account_assignments",
                    column: "MailAccountId");

                migrationBuilder.CreateIndex(
                    name: "ix_local_mail_folders_account_parent_name",
                    table: "local_mail_folders",
                    columns: new[] { "MailboxAccountId", "ParentId", "NameKey" },
                    unique: true,
                    filter: "\"ErasedAt\" IS NULL")
                    .Annotation("Npgsql:NullsDistinct", false);

                migrationBuilder.CreateIndex(
                    name: "ix_local_mail_folders_account_role",
                    table: "local_mail_folders",
                    columns: new[] { "MailboxAccountId", "Role" },
                    unique: true,
                    filter: "\"Role\" IS NOT NULL AND \"ErasedAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "IX_local_mail_folders_MailboxAccountId_ErasedAt",
                    table: "local_mail_folders",
                    columns: new[] { "MailboxAccountId", "ErasedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_account",
                    table: "jobs",
                    columns: new[] { "MailboxAccountId", "EnqueuedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_account_turn",
                    table: "jobs",
                    columns: new[] { "MailboxAccountId", "TurnAt" },
                    filter: "\"State\" IN ('Pending', 'Claimed')");

                migrationBuilder.CreateIndex(
                    name: "IX_email_threads_MailboxAccountId",
                    table: "email_threads",
                    column: "MailboxAccountId");

                migrationBuilder.AddForeignKey(
                    name: "FK_email_threads_mailbox_accounts_MailboxAccountId",
                    table: "email_threads",
                    column: "MailboxAccountId",
                    principalTable: "mailbox_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_jobs_mailbox_accounts_MailboxAccountId",
                    table: "jobs",
                    column: "MailboxAccountId",
                    principalTable: "mailbox_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_local_mail_folders_mailbox_accounts_MailboxAccountId",
                    table: "local_mail_folders",
                    column: "MailboxAccountId",
                    principalTable: "mailbox_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                    table: "mail_folders",
                    column: "MailboxAccountId",
                    principalTable: "mailbox_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_email_threads_mailbox_accounts_MailboxAccountId",
                    table: "email_threads");

                migrationBuilder.DropForeignKey(
                    name: "FK_jobs_mailbox_accounts_MailboxAccountId",
                    table: "jobs");

                migrationBuilder.DropForeignKey(
                    name: "FK_local_mail_folders_mailbox_accounts_MailboxAccountId",
                    table: "local_mail_folders");

                migrationBuilder.DropForeignKey(
                    name: "FK_mail_folders_mailbox_accounts_MailboxAccountId",
                    table: "mail_folders");

                migrationBuilder.DropTable(
                    name: "account_stored_content");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_account_identity",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_account_timeline",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_attachment_text",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_filed_sent_copy",
                    table: "stored_emails");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_spam_classification_runs",
                    table: "spam_classification_runs");

                migrationBuilder.DropIndex(
                    name: "ix_recurring_sends_identity",
                    table: "recurring_sends");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_identity",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_outstanding",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings");

                migrationBuilder.DropIndex(
                    name: "ix_outgoing_email_filings_recent_by_filing",
                    table: "outgoing_email_filings");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_mailbox_refresh_tokens",
                    table: "mailbox_refresh_tokens");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_outstanding",
                    table: "mailbox_mutations");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutation_audit_entries_account_completed",
                    table: "mailbox_mutation_audit_entries");

                migrationBuilder.DropPrimaryKey(
                    name: "PK_mailbox_accounts",
                    table: "mailbox_accounts");

                migrationBuilder.DropIndex(
                    name: "ix_mail_rule_executions_account_evaluated",
                    table: "mail_rule_executions");

                migrationBuilder.DropIndex(
                    name: "ix_mail_rule_executions_account_rule_evaluated",
                    table: "mail_rule_executions");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rule_evaluation_runs",
                    table: "mail_rule_evaluation_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rederivation_runs",
                    table: "mail_rederivation_runs");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_mail_rederivation_positions",
                    table: "mail_rederivation_positions");

                migrationBuilder.DropIndex(
                    name: "ix_mail_folders_account_alias_generation",
                    table: "mail_folders");

                migrationBuilder.DropIndex(
                    name: "ix_mail_answering_audit_entries_account_completed",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropIndex(
                    name: "ix_mail_answering_audit_entries_run_account",
                    table: "mail_answering_audit_entries");

                migrationBuilder.DropIndex(
                    name: "ix_mail_account_assignments_mail_account_id",
                    table: "mail_account_assignments");

                migrationBuilder.DropIndex(
                    name: "ix_local_mail_folders_account_parent_name",
                    table: "local_mail_folders");

                migrationBuilder.DropIndex(
                    name: "ix_local_mail_folders_account_role",
                    table: "local_mail_folders");

                migrationBuilder.DropIndex(
                    name: "IX_local_mail_folders_MailboxAccountId_ErasedAt",
                    table: "local_mail_folders");

                migrationBuilder.DropIndex(
                    name: "ix_jobs_account",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "ix_jobs_account_turn",
                    table: "jobs");

                migrationBuilder.DropIndex(
                    name: "IX_email_threads_MailboxAccountId",
                    table: "email_threads");

                migrationBuilder.DropPrimaryKey(
                    name: "pk_email_thread_identifiers",
                    table: "email_thread_identifiers");

                migrationBuilder.DropColumn(
                    name: "MailboxAccountId",
                    table: "stored_content_claims");

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "stored_emails",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "stored_content_claims",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "spam_classification_runs",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: "outgoing_emails",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "outgoing_email_filings",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mailbox_refresh_tokens",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mailbox_mutations",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mailbox_mutation_audit_entries",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mailbox_accounts",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_rule_executions",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_rule_evaluation_runs",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_rederivation_runs",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_rederivation_positions",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_folders",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "mail_answering_audit_entries",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "local_mail_folders",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "jobs",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "email_threads",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "email_thread_identifiers",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

                migrationBuilder.AddPrimaryKey(
                    name: "pk_spam_classification_runs",
                    table: "spam_classification_runs",
                    columns: new[] { "UserId", "MailboxAccountId" });

                migrationBuilder.AddPrimaryKey(
                    name: "PK_mailbox_refresh_tokens",
                    table: "mailbox_refresh_tokens",
                    columns: new[] { "UserId", "MailboxAccountId" });

                migrationBuilder.AddPrimaryKey(
                    name: "PK_mailbox_accounts",
                    table: "mailbox_accounts",
                    columns: new[] { "UserId", "Id" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rule_evaluation_runs",
                    table: "mail_rule_evaluation_runs",
                    columns: new[] { "UserId", "MailboxAccountId" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rederivation_runs",
                    table: "mail_rederivation_runs",
                    columns: new[] { "UserId", "MailboxAccountId", "FolderAlias" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_mail_rederivation_positions",
                    table: "mail_rederivation_positions",
                    columns: new[] { "UserId", "MailboxAccountId", "FolderAlias" });

                migrationBuilder.AddPrimaryKey(
                    name: "pk_email_thread_identifiers",
                    table: "email_thread_identifiers",
                    columns: new[] { "UserId", "MailboxAccountId", "IdentifierHash" });

                migrationBuilder.CreateTable(
                    name: "user_stored_content",
                    columns: table => new
                    {
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        StoredContentByteCount = table.Column<long>(type: "bigint", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_user_stored_content", x => x.UserId);
                        table.ForeignKey(
                            name: "FK_user_stored_content_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_attachment_text",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "Id" },
                    filter: "\"AttachmentTextDerivedAt\" IS NULL AND \"AttachmentCount\" > 0");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_rule_evaluation",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "Id" },
                    filter: "\"RulesEvaluatedAt\" IS NULL AND \"FiledFromOutgoingEmailId\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_filed_sent_copy",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                    filter: "\"FiledFromOutgoingEmailId\" IS NOT NULL AND \"UidValidity\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_user_account_identity",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_user_account_timeline",
                    table: "stored_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "ReceivedAt", "Id" },
                    descending: new[] { false, false, true, true })
                    .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

                migrationBuilder.CreateIndex(
                    name: "ix_recurring_sends_identity",
                    table: "recurring_sends",
                    columns: new[] { "UserId", "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_claimable",
                    table: "outgoing_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "AvailableAt", "Id" },
                    filter: "\"Stage\" = 'Recorded'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_identity",
                    table: "outgoing_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "RequesterOrigin", "RequesterIdentity" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_outstanding",
                    table: "outgoing_emails",
                    columns: new[] { "UserId", "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" NOT IN ('Sent', 'Refused', 'Cancelled')");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_emails_period_usage",
                    table: "outgoing_emails",
                    columns: new[] { "RecordedAt", "UserId", "MailboxAccountId" });

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_message_id",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "InternetMessageId" },
                    filter: "\"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_placement",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "FolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_outgoing_email_filings_recent_by_filing",
                    table: "outgoing_email_filings",
                    columns: new[] { "UserId", "MailboxAccountId", "Filing", "AppendedAt", "OutgoingEmailId" },
                    filter: "\"Stage\" = 'Confirmed'");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_outstanding",
                    table: "mailbox_mutations",
                    columns: new[] { "UserId", "MailboxAccountId", "RecordedAt" },
                    filter: "\"Stage\" <> 'Completed'");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_placement",
                    table: "mailbox_mutations",
                    columns: new[] { "UserId", "MailboxAccountId", "DestinationFolderPath", "PlacementUidValidity", "PlacementUid" },
                    filter: "\"PlacementObservedAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutation_audit_entries_user_account_completed",
                    table: "mailbox_mutation_audit_entries",
                    columns: new[] { "UserId", "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_user_account_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "UserId", "MailboxAccountId", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_rule_executions_user_account_rule_evaluated",
                    table: "mail_rule_executions",
                    columns: new[] { "UserId", "MailboxAccountId", "RuleName", "EvaluatedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_folders_user_account_alias_generation",
                    table: "mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId", "Alias", "ResolutionGeneration" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_run_user_account",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "RunId", "UserId", "MailboxAccountId" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mail_answering_audit_entries_user_account_completed",
                    table: "mail_answering_audit_entries",
                    columns: new[] { "UserId", "MailboxAccountId", "CompletedAt", "Id" });

                migrationBuilder.CreateIndex(
                    name: "ix_mail_account_assignments_mail_account_id",
                    table: "mail_account_assignments",
                    column: "MailAccountId",
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_local_mail_folders_user_account_parent_name",
                    table: "local_mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId", "ParentId", "NameKey" },
                    unique: true,
                    filter: "\"ErasedAt\" IS NULL")
                    .Annotation("Npgsql:NullsDistinct", false);

                migrationBuilder.CreateIndex(
                    name: "ix_local_mail_folders_user_account_role",
                    table: "local_mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId", "Role" },
                    unique: true,
                    filter: "\"Role\" IS NOT NULL AND \"ErasedAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "IX_local_mail_folders_UserId_MailboxAccountId_ErasedAt",
                    table: "local_mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId", "ErasedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_user_account",
                    table: "jobs",
                    columns: new[] { "UserId", "MailboxAccountId", "EnqueuedAt" });

                migrationBuilder.CreateIndex(
                    name: "ix_jobs_user_turn",
                    table: "jobs",
                    columns: new[] { "UserId", "TurnAt" },
                    filter: "\"State\" IN ('Pending', 'Claimed')");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_jobs_account_user",
                    table: "jobs",
                    sql: "(\"UserId\" IS NULL) = (\"MailboxAccountId\" IS NULL)");

                migrationBuilder.CreateIndex(
                    name: "IX_email_threads_UserId_MailboxAccountId",
                    table: "email_threads",
                    columns: new[] { "UserId", "MailboxAccountId" });

                migrationBuilder.AddForeignKey(
                    name: "FK_email_threads_mailbox_accounts_UserId_MailboxAccountId",
                    table: "email_threads",
                    columns: new[] { "UserId", "MailboxAccountId" },
                    principalTable: "mailbox_accounts",
                    principalColumns: new[] { "UserId", "Id" },
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_jobs_mailbox_accounts_UserId_MailboxAccountId",
                    table: "jobs",
                    columns: new[] { "UserId", "MailboxAccountId" },
                    principalTable: "mailbox_accounts",
                    principalColumns: new[] { "UserId", "Id" },
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_local_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                    table: "local_mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId" },
                    principalTable: "mailbox_accounts",
                    principalColumns: new[] { "UserId", "Id" },
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_mail_folders_mailbox_accounts_UserId_MailboxAccountId",
                    table: "mail_folders",
                    columns: new[] { "UserId", "MailboxAccountId" },
                    principalTable: "mailbox_accounts",
                    principalColumns: new[] { "UserId", "Id" },
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_mailbox_accounts_settings_accounts_UserId",
                    table: "mailbox_accounts",
                    column: "UserId",
                    principalTable: "settings_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
        }

        /// <summary>The <c>SplitContactBooksByHolder</c> migration, as it was added.</summary>
        private static class SplitContactBooksByHolder
        {
            /// <summary>Applies the migration.</summary>
            /// <remarks>
            /// <para>
            /// A contact book stops being one user's and becomes either a user's own or a mail account's, so every row has
            /// to say which. A contact somebody wrote down carries over unchanged, filed under the user it was already
            /// filed under. A collected one has no account column to move into, because nothing in the old schema recorded
            /// which mailbox picked the address up — so it is carried over only where the deployment itself answers that:
            /// where the user it was filed under is assigned exactly one mailbox and that mailbox is assigned exactly that
            /// one user, the mailbox it arrived on is the only one it can have arrived on.
            /// </para>
            /// <para>
            /// Everything else collected is deleted rather than attributed to a mailbox this migration would have to
            /// guess — a wrong guess would publish one user's correspondents to everybody else assigned a shared mailbox,
            /// which is the disclosure this whole change exists to prevent. Collection rebuilds those rows from the mail
            /// that arrives next, which is the same thing it does after <c>mfctl contact delete-collected</c>. Nothing a
            /// user or an operator wrote down is touched either way.
            /// </para>
            /// </remarks>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                ArgumentNullException.ThrowIfNull(migrationBuilder);

                // The old shape's key and indexes come off first, because the data work below moves a row between books —
                // which a foreign key naming the user, and a uniqueness scoped to one, would each refuse.
                migrationBuilder.DropForeignKey(
                    name: "FK_contact_addresses_contacts_ContactId_UserId",
                    table: "contact_addresses");

                migrationBuilder.DropUniqueConstraint(
                    name: "AK_contacts_Id_UserId",
                    table: "contacts");

                migrationBuilder.DropIndex(
                    name: "ix_contacts_user_display_name_sort_key_id",
                    table: "contacts");

                migrationBuilder.DropIndex(
                    name: "IX_contact_addresses_ContactId_UserId",
                    table: "contact_addresses");

                migrationBuilder.DropIndex(
                    name: "ix_contact_addresses_user_normalized_address",
                    table: "contact_addresses");

                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: "contacts",
                    type: "uuid",
                    nullable: true,
                    oldClrType: typeof(Guid),
                    oldType: "uuid");

                // Added nullable and tightened once they are filled, so no column default outlives the backfill: a default
                // the model never declared would sit in the schema forever and let a later insert that forgot the book key
                // succeed.
                migrationBuilder.AddColumn<string>(
                    name: "BookHolderId",
                    table: "contacts",
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "MailboxAccountId",
                    table: "contacts",
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "BookHolderId",
                    table: "contact_addresses",
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true);

                // The one case the deployment answers on its own: a user assigned exactly one mailbox that is assigned
                // exactly that one user. The mailbox row is joined rather than assumed, so a collected contact is only ever
                // moved onto an account the mail graph actually holds — which is also what the new foreign key requires.
                migrationBuilder.Sql(
                    """
                    WITH sole_assignment AS (
                        SELECT assignment."UserId" AS user_id, assignment."MailAccountId"::text AS account_id
                        FROM mail_account_assignments assignment
                        WHERE (
                                SELECT count(*) FROM mail_account_assignments held
                                WHERE held."UserId" = assignment."UserId") = 1
                          AND (
                                SELECT count(*) FROM mail_account_assignments shared
                                WHERE shared."MailAccountId" = assignment."MailAccountId") = 1)
                    UPDATE contacts
                    SET "MailboxAccountId" = sole_assignment.account_id, "UserId" = NULL
                    FROM sole_assignment
                    JOIN mailbox_accounts ON mailbox_accounts."Id" = sole_assignment.account_id
                    WHERE contacts."Origin" = 'Collected' AND contacts."UserId" = sole_assignment.user_id;
                    """);

                // The addresses go first and by hand, the foreign key that would have cascaded them having been dropped
                // above.
                migrationBuilder.Sql(
                    """
                    DELETE FROM contact_addresses
                    WHERE "ContactId" IN (
                        SELECT "Id" FROM contacts WHERE "Origin" = 'Collected' AND "UserId" IS NOT NULL);
                    """);

                migrationBuilder.Sql("""DELETE FROM contacts WHERE "Origin" = 'Collected' AND "UserId" IS NOT NULL;""");

                // The book key repeats whichever holder the row ended up with, and an address row takes its contact's
                // rather than its own former user column: a carried-over contact is in the account's book now, and its
                // addresses belong to the same book by construction.
                migrationBuilder.Sql(
                    """
                    UPDATE contacts
                    SET "BookHolderId" = CASE
                        WHEN "UserId" IS NULL THEN 'account:' || "MailboxAccountId"
                        ELSE 'user:' || "UserId"::text END;
                    """);

                migrationBuilder.Sql(
                    """
                    UPDATE contact_addresses
                    SET "BookHolderId" = contacts."BookHolderId"
                    FROM contacts
                    WHERE contacts."Id" = contact_addresses."ContactId";
                    """);

                migrationBuilder.AlterColumn<string>(
                    name: "BookHolderId",
                    table: "contacts",
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(256)",
                    oldMaxLength: 256,
                    oldNullable: true);

                migrationBuilder.AlterColumn<string>(
                    name: "BookHolderId",
                    table: "contact_addresses",
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(256)",
                    oldMaxLength: 256,
                    oldNullable: true);

                migrationBuilder.DropColumn(
                    name: "UserId",
                    table: "contact_addresses");

                migrationBuilder.AddUniqueConstraint(
                    name: "AK_contacts_Id_BookHolderId",
                    table: "contacts",
                    columns: new[] { "Id", "BookHolderId" });

                migrationBuilder.CreateIndex(
                    name: "ix_contacts_book_holder_display_name_sort_key_id",
                    table: "contacts",
                    columns: new[] { "BookHolderId", "DisplayNameSortKey", "Id" });

                migrationBuilder.CreateIndex(
                    name: "IX_contacts_MailboxAccountId",
                    table: "contacts",
                    column: "MailboxAccountId");

                migrationBuilder.CreateIndex(
                    name: "IX_contacts_UserId",
                    table: "contacts",
                    column: "UserId");

                migrationBuilder.AddCheckConstraint(
                    name: "ck_contacts_book_holder",
                    table: "contacts",
                    sql: "((\"UserId\" IS NOT NULL)::int + (\"MailboxAccountId\" IS NOT NULL)::int) = 1\nAND \"BookHolderId\" = CASE WHEN \"UserId\" IS NULL THEN 'account:' || \"MailboxAccountId\" ELSE 'user:' || \"UserId\"::text END\nAND \"Origin\" = CASE WHEN \"UserId\" IS NULL THEN 'Collected' ELSE 'Asserted' END");

                migrationBuilder.CreateIndex(
                    name: "ix_contact_addresses_book_holder_normalized_address",
                    table: "contact_addresses",
                    columns: new[] { "BookHolderId", "NormalizedAddress" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "IX_contact_addresses_ContactId_BookHolderId",
                    table: "contact_addresses",
                    columns: new[] { "ContactId", "BookHolderId" });

                migrationBuilder.AddForeignKey(
                    name: "FK_contact_addresses_contacts_ContactId_BookHolderId",
                    table: "contact_addresses",
                    columns: new[] { "ContactId", "BookHolderId" },
                    principalTable: "contacts",
                    principalColumns: new[] { "Id", "BookHolderId" },
                    onDelete: ReferentialAction.Cascade);

                migrationBuilder.AddForeignKey(
                    name: "FK_contacts_mailbox_accounts_MailboxAccountId",
                    table: "contacts",
                    column: "MailboxAccountId",
                    principalTable: "mailbox_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }

            /// <summary>Reverts the migration.</summary>
            /// <remarks>
            /// The mirror of the loss above, in the other direction: a book that belongs to a mail account has no user
            /// column to move into, so those rows go rather than being attributed to whoever happens to be assigned that
            /// mailbox.
            /// </remarks>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                ArgumentNullException.ThrowIfNull(migrationBuilder);

                migrationBuilder.DropForeignKey(
                    name: "FK_contact_addresses_contacts_ContactId_BookHolderId",
                    table: "contact_addresses");

                migrationBuilder.DropForeignKey(
                    name: "FK_contacts_mailbox_accounts_MailboxAccountId",
                    table: "contacts");

                migrationBuilder.DropUniqueConstraint(
                    name: "AK_contacts_Id_BookHolderId",
                    table: "contacts");

                migrationBuilder.DropIndex(
                    name: "ix_contacts_book_holder_display_name_sort_key_id",
                    table: "contacts");

                migrationBuilder.DropIndex(
                    name: "IX_contacts_MailboxAccountId",
                    table: "contacts");

                migrationBuilder.DropIndex(
                    name: "IX_contacts_UserId",
                    table: "contacts");

                migrationBuilder.DropCheckConstraint(
                    name: "ck_contacts_book_holder",
                    table: "contacts");

                migrationBuilder.Sql("""DELETE FROM contacts WHERE "MailboxAccountId" IS NOT NULL;""");

                migrationBuilder.DropIndex(
                    name: "ix_contact_addresses_book_holder_normalized_address",
                    table: "contact_addresses");

                migrationBuilder.DropIndex(
                    name: "IX_contact_addresses_ContactId_BookHolderId",
                    table: "contact_addresses");

                migrationBuilder.DropColumn(
                    name: "BookHolderId",
                    table: "contacts");

                migrationBuilder.DropColumn(
                    name: "MailboxAccountId",
                    table: "contacts");

                migrationBuilder.DropColumn(
                    name: "BookHolderId",
                    table: "contact_addresses");

                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: "contacts",
                    type: "uuid",
                    nullable: false,
                    defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "UserId",
                    table: "contact_addresses",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.Sql(
                    """
                    UPDATE contact_addresses
                    SET "UserId" = contacts."UserId"
                    FROM contacts
                    WHERE contacts."Id" = contact_addresses."ContactId";
                    """);

                migrationBuilder.AlterColumn<Guid>(
                    name: "UserId",
                    table: "contact_addresses",
                    type: "uuid",
                    nullable: false,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);

                migrationBuilder.AddUniqueConstraint(
                    name: "AK_contacts_Id_UserId",
                    table: "contacts",
                    columns: new[] { "Id", "UserId" });

                migrationBuilder.CreateIndex(
                    name: "ix_contacts_user_display_name_sort_key_id",
                    table: "contacts",
                    columns: new[] { "UserId", "DisplayNameSortKey", "Id" });

                migrationBuilder.CreateIndex(
                    name: "IX_contact_addresses_ContactId_UserId",
                    table: "contact_addresses",
                    columns: new[] { "ContactId", "UserId" });

                migrationBuilder.CreateIndex(
                    name: "ix_contact_addresses_user_normalized_address",
                    table: "contact_addresses",
                    columns: new[] { "UserId", "NormalizedAddress" },
                    unique: true);

                migrationBuilder.AddForeignKey(
                    name: "FK_contact_addresses_contacts_ContactId_UserId",
                    table: "contact_addresses",
                    columns: new[] { "ContactId", "UserId" },
                    principalTable: "contacts",
                    principalColumns: new[] { "Id", "UserId" },
                    onDelete: ReferentialAction.Cascade);
            }
        }

        /// <summary>The <c>AddMailboxExports</c> migration, as it was added.</summary>
        private static class AddMailboxExports
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "mailbox_exports",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        FolderPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                        State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        MessageCount = table.Column<long>(type: "bigint", nullable: false),
                        ByteCount = table.Column<long>(type: "bigint", nullable: false),
                        ArchiveByteLength = table.Column<long>(type: "bigint", nullable: true),
                        ObjectLocator = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        FailureCode = table.Column<int>(type: "integer", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_mailbox_exports", x => x.Id);
                        table.ForeignKey(
                            name: "fk_mailbox_exports_mailbox_accounts",
                            column: x => x.MailboxAccountId,
                            principalTable: "mailbox_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_exports_account_requested",
                    table: "mailbox_exports",
                    columns: new[] { "MailboxAccountId", "RequestedAt" },
                    descending: new[] { false, true });

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_exports_expires_at",
                    table: "mailbox_exports",
                    column: "ExpiresAt",
                    filter: "\"ExpiresAt\" IS NOT NULL");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mailbox_exports");
            }
        }

        /// <summary>The <c>AddRequestedCustodyAndSourceDrain</c> migration, as it was added.</summary>
        private static class AddRequestedCustodyAndSourceDrain
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "ContentVerifiedAt",
                    table: "stored_emails",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "RequestedCustody",
                    table: "mailbox_accounts",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValueSql: "'MirrorSource'");

                migrationBuilder.CreateTable(
                    name: "mailbox_source_removals",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                        UidValidity = table.Column<long>(type: "bigint", nullable: false),
                        Uid = table.Column<long>(type: "bigint", nullable: false),
                        RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mailbox_source_removals", x => x.Id);
                        table.ForeignKey(
                            name: "FK_mailbox_source_removals_mail_folders_MailFolderId",
                            column: x => x.MailFolderId,
                            principalTable: "mail_folders",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_stored_emails_awaiting_drain",
                    table: "stored_emails",
                    columns: new[] { "MailboxAccountId", "ReceivedAt", "Id" },
                    filter: "\"UidValidity\" IS NOT NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_source_removals_occurrence",
                    table: "mailbox_source_removals",
                    columns: new[] { "MailFolderId", "UidValidity", "Uid" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_source_removals_queue",
                    table: "mailbox_source_removals",
                    columns: new[] { "MailboxAccountId", "RecordedAt" });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mailbox_source_removals");

                migrationBuilder.DropIndex(
                    name: "ix_stored_emails_awaiting_drain",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "ContentVerifiedAt",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "RequestedCustody",
                    table: "mailbox_accounts");
            }
        }

        /// <summary>The <c>FlagOnlyAuthoredDeletes</c> migration, as it was added.</summary>
        private static class FlagOnlyAuthoredDeletes
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "AuthoredDeleteFlaggedAt",
                    table: "stored_emails",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<DateTimeOffset>(
                    name: "DeleteFlagSettledAt",
                    table: "mailbox_mutations",
                    type: "timestamp with time zone",
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "ServerDisposition",
                    table: "mailbox_mutations",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: true);

                // Every delete recorded before the setting existed expunged its source, so the rows say so rather than
                // leaving the answer to the mapping's reading of a missing value.
                migrationBuilder.Sql(
                    """UPDATE mailbox_mutations SET "ServerDisposition" = 'Expunge' WHERE "Mutation" = 'delete';""");

                migrationBuilder.CreateTable(
                    name: "mailbox_flagged_deletes",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        MailFolderId = table.Column<long>(type: "bigint", nullable: false),
                        UidValidity = table.Column<long>(type: "bigint", nullable: false),
                        Uid = table.Column<long>(type: "bigint", nullable: false),
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                        LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mailbox_flagged_deletes", x => x.Id);
                        table.ForeignKey(
                            name: "FK_mailbox_flagged_deletes_mail_folders_MailFolderId",
                            column: x => x.MailFolderId,
                            principalTable: "mail_folders",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "FK_mailbox_flagged_deletes_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_mutations_flagged_delete",
                    table: "mailbox_mutations",
                    columns: new[] { "MailFolderId", "UidValidity", "RecordedAt" },
                    filter: "\"ServerDisposition\" = 'FlagDeleted' AND \"Stage\" = 'Completed' AND \"DeleteFlagSettledAt\" IS NULL AND \"SourceRemovalObservedAt\" IS NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_flagged_deletes_occurrence",
                    table: "mailbox_flagged_deletes",
                    columns: new[] { "MailFolderId", "UidValidity", "Uid" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_flagged_deletes_queue",
                    table: "mailbox_flagged_deletes",
                    columns: new[] { "MailFolderId", "UidValidity", "LastObservedAt" });

                migrationBuilder.CreateIndex(
                    name: "IX_mailbox_flagged_deletes_StoredEmailId",
                    table: "mailbox_flagged_deletes",
                    column: "StoredEmailId");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mailbox_flagged_deletes");

                migrationBuilder.DropIndex(
                    name: "ix_mailbox_mutations_flagged_delete",
                    table: "mailbox_mutations");

                migrationBuilder.DropColumn(
                    name: "AuthoredDeleteFlaggedAt",
                    table: "stored_emails");

                migrationBuilder.DropColumn(
                    name: "DeleteFlagSettledAt",
                    table: "mailbox_mutations");

                migrationBuilder.DropColumn(
                    name: "ServerDisposition",
                    table: "mailbox_mutations");
            }
        }

        /// <summary>The <c>AddTasks</c> migration, as it was added.</summary>
        private static class AddTasks
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "tasks",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                        DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                        Origin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                        SourceStoredEmailId = table.Column<Guid>(type: "uuid", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_tasks", x => x.Id);
                        table.ForeignKey(
                            name: "FK_tasks_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_tasks_user_due",
                    table: "tasks",
                    columns: new[] { "UserId", "DueOn", "Id" });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "tasks");
            }
        }

        /// <summary>The <c>AddCalendarEvents</c> migration, as it was added.</summary>
        private static class AddCalendarEvents
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "calendar_events",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                        StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        SourceStoredEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                        ImportedUid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                        RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        AmendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_calendar_events", x => x.Id);
                        table.ForeignKey(
                            name: "FK_calendar_events_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_calendar_events_user_imported_uid",
                    table: "calendar_events",
                    columns: new[] { "UserId", "ImportedUid" },
                    unique: true,
                    filter: "\"ImportedUid\" IS NOT NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_calendar_events_user_starts_at_id",
                    table: "calendar_events",
                    columns: new[] { "UserId", "StartsAt", "Id" });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "calendar_events");
            }
        }

        /// <summary>The <c>AddCalendarEventReminders</c> migration, as it was added.</summary>
        private static class AddCalendarEventReminders
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "TargetCalendarEventId",
                    table: "notifications",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<bool>(
                    name: "IsAllDay",
                    table: "calendar_events",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);

                migrationBuilder.CreateTable(
                    name: "calendar_event_reminders",
                    columns: table => new
                    {
                        CalendarEventId = table.Column<Guid>(type: "uuid", nullable: false),
                        MinutesBefore = table.Column<int>(type: "integer", nullable: false),
                        DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        RaisedForDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_calendar_event_reminders", x => new { x.CalendarEventId, x.MinutesBefore });
                        table.ForeignKey(
                            name: "FK_calendar_event_reminders_calendar_events_CalendarEventId",
                            column: x => x.CalendarEventId,
                            principalTable: "calendar_events",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_notifications_TargetCalendarEventId",
                    table: "notifications",
                    column: "TargetCalendarEventId");

                migrationBuilder.CreateIndex(
                    name: "ix_calendar_event_reminders_due_at",
                    table: "calendar_event_reminders",
                    column: "DueAt",
                    filter: "\"RaisedForDueAt\" IS NULL");

                migrationBuilder.AddForeignKey(
                    name: "FK_notifications_calendar_events_TargetCalendarEventId",
                    table: "notifications",
                    column: "TargetCalendarEventId",
                    principalTable: "calendar_events",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_notifications_calendar_events_TargetCalendarEventId",
                    table: "notifications");

                migrationBuilder.DropTable(
                    name: "calendar_event_reminders");

                migrationBuilder.DropIndex(
                    name: "IX_notifications_TargetCalendarEventId",
                    table: "notifications");

                migrationBuilder.DropColumn(
                    name: "TargetCalendarEventId",
                    table: "notifications");

                migrationBuilder.DropColumn(
                    name: "IsAllDay",
                    table: "calendar_events");
            }
        }

        /// <summary>The <c>JournalDiscoveryRuns</c> migration, as it was added.</summary>
        private static class JournalDiscoveryRuns
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "discovery_runs",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        StopRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_discovery_runs", x => x.Id);
                        table.ForeignKey(
                            name: "FK_discovery_runs_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "discovery_run_events",
                    columns: table => new
                    {
                        RunId = table.Column<Guid>(type: "uuid", nullable: false),
                        Sequence = table.Column<long>(type: "bigint", nullable: false),
                        Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Payload = table.Column<string>(type: "jsonb", nullable: false),
                        WrittenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_discovery_run_events", x => new { x.RunId, x.Sequence });
                        table.ForeignKey(
                            name: "FK_discovery_run_events_discovery_runs_RunId",
                            column: x => x.RunId,
                            principalTable: "discovery_runs",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_discovery_runs_UserId",
                    table: "discovery_runs",
                    column: "UserId");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "discovery_run_events");

                migrationBuilder.DropTable(
                    name: "discovery_runs");
            }
        }

        /// <summary>The <c>AddTaskReminders</c> migration, as it was added.</summary>
        private static class AddTaskReminders
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<int>(
                    name: "DueDayOffsetMinutes",
                    table: "tasks",
                    type: "integer",
                    nullable: true);

                migrationBuilder.AddColumn<Guid>(
                    name: "TargetPersonalTaskId",
                    table: "notifications",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.CreateTable(
                    name: "task_reminders",
                    columns: table => new
                    {
                        PersonalTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                        MinutesBefore = table.Column<int>(type: "integer", nullable: false),
                        DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        RaisedForDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_task_reminders", x => new { x.PersonalTaskId, x.MinutesBefore });
                        table.ForeignKey(
                            name: "FK_task_reminders_tasks_PersonalTaskId",
                            column: x => x.PersonalTaskId,
                            principalTable: "tasks",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_notifications_TargetPersonalTaskId",
                    table: "notifications",
                    column: "TargetPersonalTaskId");

                migrationBuilder.CreateIndex(
                    name: "ix_task_reminders_due_at",
                    table: "task_reminders",
                    column: "DueAt",
                    filter: "\"RaisedForDueAt\" IS NULL");

                migrationBuilder.AddForeignKey(
                    name: "FK_notifications_tasks_TargetPersonalTaskId",
                    table: "notifications",
                    column: "TargetPersonalTaskId",
                    principalTable: "tasks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_notifications_tasks_TargetPersonalTaskId",
                    table: "notifications");

                migrationBuilder.DropTable(
                    name: "task_reminders");

                migrationBuilder.DropIndex(
                    name: "IX_notifications_TargetPersonalTaskId",
                    table: "notifications");

                migrationBuilder.DropColumn(
                    name: "DueDayOffsetMinutes",
                    table: "tasks");

                migrationBuilder.DropColumn(
                    name: "TargetPersonalTaskId",
                    table: "notifications");
            }
        }

        /// <summary>The <c>StoreDiscoveryRunEventPayloadAsWritten</c> migration, as it was added.</summary>
        private static class StoreDiscoveryRunEventPayloadAsWritten
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterColumn<string>(
                    name: "Payload",
                    table: "discovery_run_events",
                    type: "json",
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "jsonb");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterColumn<string>(
                    name: "Payload",
                    table: "discovery_run_events",
                    type: "jsonb",
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "json");
            }
        }

        /// <summary>The <c>AddAgentConversations</c> migration, as it was added.</summary>
        private static class AddAgentConversations
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "agent_conversations",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                        StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        Sequence = table.Column<long>(type: "bigint", nullable: false),
                        ComposingMessageId = table.Column<Guid>(type: "uuid", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_agent_conversations", x => x.Id);
                        table.ForeignKey(
                            name: "FK_agent_conversations_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "agent_conversation_entries",
                    columns: table => new
                    {
                        ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                        Sequence = table.Column<long>(type: "bigint", nullable: false),
                        Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                        Payload = table.Column<string>(type: "json", nullable: false),
                        AnsweredProposalAt = table.Column<long>(type: "bigint", nullable: true),
                        ProposalState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                        WrittenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_agent_conversation_entries", x => new { x.ConversationId, x.Sequence });
                        table.ForeignKey(
                            name: "FK_agent_conversation_entries_agent_conversations_Conversation~",
                            column: x => x.ConversationId,
                            principalTable: "agent_conversations",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_agent_conversation_entries_answered",
                    table: "agent_conversation_entries",
                    columns: new[] { "ConversationId", "AnsweredProposalAt" },
                    filter: "\"AnsweredProposalAt\" IS NOT NULL");

                migrationBuilder.CreateIndex(
                    name: "ix_agent_conversations_user_last_activity",
                    table: "agent_conversations",
                    columns: new[] { "UserId", "LastActivityAt" },
                    descending: new[] { false, true });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "agent_conversation_entries");

                migrationBuilder.DropTable(
                    name: "agent_conversations");
            }
        }

        /// <summary>The <c>AddMailboxMutationLocalChange</c> migration, as it was added.</summary>
        private static class AddMailboxMutationLocalChange
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "LocalChange",
                    table: "mailbox_mutations",
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: false,
                    defaultValue: "None");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "LocalChange",
                    table: "mailbox_mutations");
            }
        }

        /// <summary>The <c>AddMailboxRestoreAppends</c> migration, as it was added.</summary>
        private static class AddMailboxRestoreAppends
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<int>(
                    name: "RestoreGeneration",
                    table: "mailbox_accounts",
                    type: "integer",
                    nullable: false,
                    defaultValue: 0);

                migrationBuilder.AddColumn<Guid>(
                    name: "RestoreStatePosition",
                    table: "mailbox_accounts",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.CreateTable(
                    name: "mailbox_restore_appends",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        MailboxAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                        FolderAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        FolderGeneration = table.Column<int>(type: "integer", nullable: false),
                        IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        AppendedUidValidity = table.Column<long>(type: "bigint", nullable: true),
                        AppendedUid = table.Column<long>(type: "bigint", nullable: true),
                        SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mailbox_restore_appends", x => x.Id);
                        table.ForeignKey(
                            name: "FK_mailbox_restore_appends_stored_emails_StoredEmailId",
                            column: x => x.StoredEmailId,
                            principalTable: "stored_emails",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_restore_appends_email",
                    table: "mailbox_restore_appends",
                    column: "StoredEmailId",
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "ix_mailbox_restore_appends_unanswered",
                    table: "mailbox_restore_appends",
                    columns: new[] { "MailboxAccountId", "IssuedAt" },
                    filter: "\"SettledAt\" IS NULL");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mailbox_restore_appends");

                migrationBuilder.DropColumn(
                    name: "RestoreGeneration",
                    table: "mailbox_accounts");

                migrationBuilder.DropColumn(
                    name: "RestoreStatePosition",
                    table: "mailbox_accounts");
            }
        }

        /// <summary>The <c>RecordAgentConversationTechnicalHistory</c> migration, as it was added.</summary>
        private static class RecordAgentConversationTechnicalHistory
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<long>(
                    name: "VisibleEntryCount",
                    table: "agent_conversations",
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L);

                migrationBuilder.AddColumn<bool>(
                    name: "Visible",
                    table: "agent_conversation_entries",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);

                // Every entry written before this migration is one a person reads, because the technical kinds arrive with
                // it, so each existing row is visible and each conversation's visible count is the count it already holds.
                migrationBuilder.Sql("""UPDATE agent_conversation_entries SET "Visible" = TRUE;""");
                migrationBuilder.Sql("""UPDATE agent_conversations SET "VisibleEntryCount" = "Sequence";""");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "VisibleEntryCount",
                    table: "agent_conversations");

                migrationBuilder.DropColumn(
                    name: "Visible",
                    table: "agent_conversation_entries");
            }
        }

        /// <summary>The <c>IndexAgentConversationsForHistorySearch</c> migration, as it was added.</summary>
        private static class IndexAgentConversationsForHistorySearch
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<NpgsqlTsVector>(
                    name: "SearchVector",
                    table: "agent_conversation_entries",
                    type: "tsvector",
                    nullable: true,
                    computedColumnSql: "CASE WHEN \"Kind\" = 'message' THEN to_tsvector('simple'::regconfig, coalesce(\"Payload\" ->> 'text', '')) WHEN \"Kind\" IN ('block', 'proposal') THEN json_to_tsvector('simple'::regconfig, \"Payload\" -> 'block', '[\"string\"]') END",
                    stored: true);

                migrationBuilder.CreateTable(
                    name: "agent_conversation_embeddings",
                    columns: table => new
                    {
                        ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                        Sequence = table.Column<long>(type: "bigint", nullable: false),
                        EmbeddingProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                        Dimension = table.Column<int>(type: "integer", nullable: false),
                        Embedding = table.Column<Vector>(type: "vector", nullable: false),
                        GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("pk_agent_conversation_embeddings", x => new { x.ConversationId, x.Sequence, x.EmbeddingProfileId });
                        table.CheckConstraint("ck_agent_conversation_embeddings_dimension", "vector_dims(\"Embedding\") = \"Dimension\"");
                        table.ForeignKey(
                            name: "fk_agent_conversation_embeddings_embedding_profiles",
                            columns: x => new { x.EmbeddingProfileId, x.Dimension },
                            principalTable: "embedding_profiles",
                            principalColumns: new[] { "Id", "Dimension" },
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "fk_agent_conversation_embeddings_entries",
                            columns: x => new { x.ConversationId, x.Sequence },
                            principalTable: "agent_conversation_entries",
                            principalColumns: new[] { "ConversationId", "Sequence" },
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_agent_conversation_entries_search_vector",
                    table: "agent_conversation_entries",
                    column: "SearchVector")
                    .Annotation("Npgsql:IndexMethod", "GIN");

                migrationBuilder.CreateIndex(
                    name: "ix_agent_conversation_embeddings_profile",
                    table: "agent_conversation_embeddings",
                    columns: new[] { "EmbeddingProfileId", "Dimension" });
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "agent_conversation_embeddings");

                migrationBuilder.DropIndex(
                    name: "ix_agent_conversation_entries_search_vector",
                    table: "agent_conversation_entries");

                migrationBuilder.DropColumn(
                    name: "SearchVector",
                    table: "agent_conversation_entries");
            }
        }

        /// <summary>The <c>ArchiveAnAgentConversation</c> migration, as it was added.</summary>
        private static class ArchiveAnAgentConversation
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<bool>(
                    name: "Archived",
                    table: "agent_conversations",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "Archived",
                    table: "agent_conversations");
            }
        }

        /// <summary>The <c>GroupMailAccountsIntoOrganizations</c> migration, as it was added.</summary>
        private static class GroupMailAccountsIntoOrganizations
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "OrganizationId",
                    table: "settings_mail_accounts",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.CreateIndex(
                    name: "ix_settings_mail_accounts_organization",
                    table: "settings_mail_accounts",
                    column: "OrganizationId");

                migrationBuilder.AddForeignKey(
                    name: "FK_settings_mail_accounts_organizations_OrganizationId",
                    table: "settings_mail_accounts",
                    column: "OrganizationId",
                    principalTable: "organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_settings_mail_accounts_organizations_OrganizationId",
                    table: "settings_mail_accounts");

                migrationBuilder.DropIndex(
                    name: "ix_settings_mail_accounts_organization",
                    table: "settings_mail_accounts");

                migrationBuilder.DropColumn(
                    name: "OrganizationId",
                    table: "settings_mail_accounts");
            }
        }

        /// <summary>The <c>AddRolesGroupsAndRoleAssignments</c> migration, as it was added.</summary>
        private static class AddRolesGroupsAndRoleAssignments
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "roles",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_roles", x => x.Id);
                    });

                migrationBuilder.CreateTable(
                    name: "user_groups",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_user_groups", x => x.Id);
                        table.ForeignKey(
                            name: "FK_user_groups_organizations_OrganizationId",
                            column: x => x.OrganizationId,
                            principalTable: "organizations",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "role_permissions",
                    columns: table => new
                    {
                        RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                        Permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_role_permissions", x => new { x.RoleId, x.Permission });
                        table.ForeignKey(
                            name: "FK_role_permissions_roles_RoleId",
                            column: x => x.RoleId,
                            principalTable: "roles",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "role_assignments",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                        PrincipalUserId = table.Column<Guid>(type: "uuid", nullable: true),
                        PrincipalGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                        ScopeOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                        ScopeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                        AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_role_assignments", x => x.Id);
                        table.CheckConstraint("ck_role_assignments_at_most_one_scope", "num_nonnulls(\"ScopeOrganizationId\", \"ScopeUserId\") <= 1");
                        table.CheckConstraint("ck_role_assignments_one_principal", "num_nonnulls(\"PrincipalUserId\", \"PrincipalGroupId\") = 1");
                        table.ForeignKey(
                            name: "fk_role_assignments_principal_group",
                            column: x => x.PrincipalGroupId,
                            principalTable: "user_groups",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "fk_role_assignments_principal_user",
                            column: x => x.PrincipalUserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "fk_role_assignments_roles",
                            column: x => x.RoleId,
                            principalTable: "roles",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "fk_role_assignments_scope_organization",
                            column: x => x.ScopeOrganizationId,
                            principalTable: "organizations",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "fk_role_assignments_scope_user",
                            column: x => x.ScopeUserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateTable(
                    name: "user_group_members",
                    columns: table => new
                    {
                        GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: false),
                        AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_user_group_members", x => new { x.GroupId, x.UserId });
                        table.ForeignKey(
                            name: "FK_user_group_members_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                        table.ForeignKey(
                            name: "FK_user_group_members_user_groups_GroupId",
                            column: x => x.GroupId,
                            principalTable: "user_groups",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_role_assignments_PrincipalGroupId",
                    table: "role_assignments",
                    column: "PrincipalGroupId");

                migrationBuilder.CreateIndex(
                    name: "IX_role_assignments_PrincipalUserId",
                    table: "role_assignments",
                    column: "PrincipalUserId");

                migrationBuilder.CreateIndex(
                    name: "ix_role_assignments_role_principal_scope",
                    table: "role_assignments",
                    columns: new[] { "RoleId", "PrincipalUserId", "PrincipalGroupId", "ScopeOrganizationId", "ScopeUserId" },
                    unique: true)
                    .Annotation("Npgsql:NullsDistinct", false);

                migrationBuilder.CreateIndex(
                    name: "IX_role_assignments_ScopeOrganizationId",
                    table: "role_assignments",
                    column: "ScopeOrganizationId");

                migrationBuilder.CreateIndex(
                    name: "IX_role_assignments_ScopeUserId",
                    table: "role_assignments",
                    column: "ScopeUserId");

                migrationBuilder.CreateIndex(
                    name: "ix_roles_name",
                    table: "roles",
                    column: "Name",
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "IX_user_group_members_UserId",
                    table: "user_group_members",
                    column: "UserId");

                migrationBuilder.CreateIndex(
                    name: "ix_user_groups_name",
                    table: "user_groups",
                    column: "Name",
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "IX_user_groups_OrganizationId",
                    table: "user_groups",
                    column: "OrganizationId");

                // The built-in roles, written once as ordinary rows an operator may rename, edit, or delete. Each list is
                // the names as this release publishes them and is stated literally rather than read from the build, so a
                // later release that publishes a permission adds it to none of them and no later migration edits them.
                // mailfathom.admin.roles.write is published by the routes that administer these records, in the same
                // release; until then it is read back as unpublished and grants nothing.
                migrationBuilder.Sql(
                    """
                    INSERT INTO roles ("Id", "Name", "CreatedAt") VALUES
                        ('01a11deb-3808-7000-8000-000000000001', 'Mail user', now()),
                        ('01a11deb-3808-7000-8000-000000000002', 'Organization administrator', now()),
                        ('01a11deb-3808-7000-8000-000000000003', 'Administrator', now());

                    INSERT INTO role_permissions ("RoleId", "Permission")
                    SELECT '01a11deb-3808-7000-8000-000000000001'::uuid, name FROM unnest(ARRAY[
                        'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                        'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                        'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                        'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write']) AS name
                    UNION ALL
                    SELECT '01a11deb-3808-7000-8000-000000000002'::uuid, name FROM unnest(ARRAY[
                        'mailfathom.admin.read', 'mailfathom.admin.audit.read', 'mailfathom.admin.operate',
                        'mailfathom.admin.credentials.write', 'mailfathom.admin.configuration.write',
                        'mailfathom.admin.erase', 'mailfathom.admin.roles.write']) AS name
                    UNION ALL
                    SELECT '01a11deb-3808-7000-8000-000000000003'::uuid, name FROM unnest(ARRAY[
                        'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                        'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                        'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                        'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write',
                        'mailfathom.admin.read', 'mailfathom.admin.audit.read', 'mailfathom.admin.operate',
                        'mailfathom.admin.credentials.write', 'mailfathom.admin.spend', 'mailfathom.admin.erase',
                        'mailfathom.admin.export', 'mailfathom.admin.configuration.write',
                        'mailfathom.admin.custody.write', 'mailfathom.admin.roles.write']) AS name;
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "role_assignments");

                migrationBuilder.DropTable(
                    name: "role_permissions");

                migrationBuilder.DropTable(
                    name: "user_group_members");

                migrationBuilder.DropTable(
                    name: "roles");

                migrationBuilder.DropTable(
                    name: "user_groups");
            }
        }

        /// <summary>The <c>HoldMailAccountSettingsInColumns</c> migration, as it was added.</summary>
        private static class HoldMailAccountSettingsInColumns
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<bool>(
                    name: "ClassifiesSpam",
                    table: "settings_mail_accounts",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);

                migrationBuilder.AddColumn<bool>(
                    name: "HasReadableSettings",
                    table: "settings_mail_accounts",
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);

                migrationBuilder.AddColumn<int[]>(
                    name: "ScansFor",
                    table: "settings_mail_accounts",
                    type: "integer[]",
                    nullable: false,
                    defaultValueSql: "'{}'::integer[]");

                migrationBuilder.AddColumn<int[]>(
                    name: "ScreensOutgoingMailFor",
                    table: "settings_mail_accounts",
                    type: "integer[]",
                    nullable: false,
                    defaultValueSql: "'{}'::integer[]");

                migrationBuilder.AddColumn<long>(
                    name: "SettingsVersion",
                    table: "settings_mail_accounts",
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L);

                migrationBuilder.AddColumn<int>(
                    name: "SynchronizationMode",
                    table: "settings_mail_accounts",
                    type: "integer",
                    nullable: false,
                    defaultValue: 0);

                migrationBuilder.CreateTable(
                    name: "mail_account_folder_settings",
                    columns: table => new
                    {
                        MailAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                        Alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                        SpecialUse = table.Column<int>(type: "integer", nullable: true),
                        IsSynchronized = table.Column<bool>(type: "boolean", nullable: false),
                        IsVisibleToTools = table.Column<bool>(type: "boolean", nullable: false),
                        GeneratesEmbeddings = table.Column<bool>(type: "boolean", nullable: false),
                        IsClassifiedForSpam = table.Column<bool>(type: "boolean", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_mail_account_folder_settings", x => new { x.MailAccountId, x.Alias });
                        table.ForeignKey(
                            name: "fk_mail_account_folder_settings_settings_mail_accounts",
                            column: x => x.MailAccountId,
                            principalTable: "settings_mail_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                // Every existing account's settings are read out of its document the way the host binds one: property names
                // in any case, a list written as an array or as an object keyed by position, a switch written as a boolean or
                // as text, an unset participation switch reading as true, no folders reading as the inbox alone, and spam
                // classification scanning the inbox aliases when it names no folders. The helpers live in pg_temp, so they
                // end with the session that ran the migration and leave nothing in the schema.
                // What this reads is an approximation of that binding: every object document is marked readable, including one
                // the host's strict binding would refuse. It stands only until each account is read again in C#, because the
                // settings version is left at zero for every row here, which is what the convergence interval reads again.
                migrationBuilder.Sql(
                    """
                    CREATE FUNCTION pg_temp.mf_member(document jsonb, name text) RETURNS jsonb
                    LANGUAGE sql IMMUTABLE AS $$
                        SELECT entry.value
                        FROM jsonb_each(CASE jsonb_typeof(document) WHEN 'object' THEN document ELSE '{}'::jsonb END) AS entry
                        WHERE lower(entry.key) = lower(name)
                        ORDER BY entry.key
                        LIMIT 1
                    $$;

                    CREATE FUNCTION pg_temp.mf_list(list jsonb) RETURNS jsonb
                    LANGUAGE sql IMMUTABLE AS $$
                        SELECT CASE jsonb_typeof(list)
                            WHEN 'array' THEN list
                            WHEN 'object' THEN (
                                SELECT coalesce(jsonb_agg(e.value ORDER BY CASE WHEN e.key ~ '^[0-9]{1,9}$' THEN e.key::int END, e.key), '[]'::jsonb)
                                FROM jsonb_each(list) AS e)
                            ELSE '[]'::jsonb
                        END
                    $$;

                    CREATE FUNCTION pg_temp.mf_text(value jsonb) RETURNS text
                    LANGUAGE sql IMMUTABLE AS $$
                        SELECT CASE WHEN jsonb_typeof(value) IN ('string', 'number', 'boolean') THEN nullif(btrim(value #>> '{}'), '') END
                    $$;

                    CREATE FUNCTION pg_temp.mf_switch(value jsonb, unset boolean) RETURNS boolean
                    LANGUAGE sql IMMUTABLE AS $$
                        SELECT coalesce(lower(pg_temp.mf_text(value)) = 'true', unset)
                    $$;

                    UPDATE settings_mail_accounts AS account
                    SET "HasReadableSettings" = jsonb_typeof(account."Document") = 'object',
                        "SynchronizationMode" = CASE lower(pg_temp.mf_text(pg_temp.mf_member(account."Document", 'Mode')))
                            WHEN 'push' THEN 1
                            WHEN '1' THEN 1
                            ELSE 0
                        END,
                        "ClassifiesSpam" = pg_temp.mf_switch(
                            pg_temp.mf_member(pg_temp.mf_member(account."Document", 'SpamClassification'), 'Enabled'),
                            false),
                        "ScansFor" = ARRAY(
                            SELECT scanner.kind
                            FROM (VALUES ('Secrets', 0), ('Pii', 1)) AS scanner(name, kind)
                            WHERE pg_temp.mf_switch(
                                pg_temp.mf_member(
                                    pg_temp.mf_member(pg_temp.mf_member(account."Document", 'SensitiveContent'), scanner.name),
                                    'Enabled'),
                                false)
                            ORDER BY scanner.kind),
                        "ScreensOutgoingMailFor" = ARRAY(
                            SELECT DISTINCT scanner.kind
                            FROM jsonb_array_elements(pg_temp.mf_list(pg_temp.mf_member(
                                pg_temp.mf_member(account."Document", 'SensitiveContent'),
                                'ScreenOutgoingMailFor'))) AS named(value)
                            JOIN (VALUES ('secrets', 0), ('pii', 1)) AS scanner(name, kind)
                                ON scanner.name = lower(pg_temp.mf_text(named.value))
                            ORDER BY scanner.kind);

                    WITH declared AS (
                        SELECT account."Id" AS account_id,
                               pg_temp.mf_member(account."Document", 'SpamClassification') AS classification,
                               CASE WHEN jsonb_array_length(pg_temp.mf_list(pg_temp.mf_member(account."Document", 'Folders'))) = 0
                                    THEN '[{"Alias": "Inbox", "SpecialUse": "Inbox"}]'::jsonb
                                    ELSE pg_temp.mf_list(pg_temp.mf_member(account."Document", 'Folders'))
                               END AS folders
                        FROM settings_mail_accounts AS account
                        WHERE account."HasReadableSettings"
                    ),
                    folder AS (
                        SELECT declared.account_id,
                               declared.classification,
                               upper(pg_temp.mf_text(pg_temp.mf_member(entry.value, 'Alias'))) AS alias,
                               role.special_use,
                               pg_temp.mf_switch(pg_temp.mf_member(entry.value, 'Synchronize'), true) AS is_synchronized,
                               pg_temp.mf_switch(pg_temp.mf_member(entry.value, 'VisibleToTools'), true) AS is_visible_to_tools,
                               pg_temp.mf_switch(pg_temp.mf_member(entry.value, 'GenerateEmbeddings'), true) AS generates_embeddings,
                               entry.position
                        FROM declared
                        CROSS JOIN LATERAL jsonb_array_elements(declared.folders) WITH ORDINALITY AS entry(value, position)
                        LEFT JOIN (VALUES ('inbox', 0), ('archive', 1), ('drafts', 2), ('sent', 3), ('junk', 4),
                                          ('trash', 5), ('all', 6), ('flagged', 7), ('important', 8), ('outbox', 9))
                            AS role(name, special_use)
                            ON role.name = lower(pg_temp.mf_text(pg_temp.mf_member(entry.value, 'SpecialUse')))
                    ),
                    usable AS (
                        SELECT DISTINCT ON (account_id, alias) *
                        FROM folder
                        WHERE alias IS NOT NULL AND length(alias) <= 128
                        ORDER BY account_id, alias, position
                    ),
                    scanned AS (
                        SELECT usable.account_id, usable.alias
                        FROM usable
                        WHERE CASE
                            WHEN jsonb_typeof(pg_temp.mf_member(usable.classification, 'ScannedFolders')) IN ('array', 'object')
                                THEN EXISTS (
                                    SELECT 1
                                    FROM jsonb_array_elements(pg_temp.mf_list(pg_temp.mf_member(usable.classification, 'ScannedFolders'))) AS named(value)
                                    WHERE upper(pg_temp.mf_text(named.value)) = usable.alias)
                            ELSE usable.special_use IS NOT DISTINCT FROM 0
                        END
                    )
                    INSERT INTO mail_account_folder_settings
                        ("MailAccountId", "Alias", "SpecialUse", "IsSynchronized", "IsVisibleToTools", "GeneratesEmbeddings", "IsClassifiedForSpam")
                    SELECT usable.account_id, usable.alias, usable.special_use, usable.is_synchronized, usable.is_visible_to_tools,
                           usable.generates_embeddings,
                           EXISTS (SELECT 1 FROM scanned WHERE scanned.account_id = usable.account_id AND scanned.alias = usable.alias)
                    FROM usable;

                    DROP FUNCTION pg_temp.mf_switch(jsonb, boolean);
                    DROP FUNCTION pg_temp.mf_text(jsonb);
                    DROP FUNCTION pg_temp.mf_list(jsonb);
                    DROP FUNCTION pg_temp.mf_member(jsonb, text);
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "mail_account_folder_settings");

                migrationBuilder.DropColumn(
                    name: "ClassifiesSpam",
                    table: "settings_mail_accounts");

                migrationBuilder.DropColumn(
                    name: "HasReadableSettings",
                    table: "settings_mail_accounts");

                migrationBuilder.DropColumn(
                    name: "ScansFor",
                    table: "settings_mail_accounts");

                migrationBuilder.DropColumn(
                    name: "ScreensOutgoingMailFor",
                    table: "settings_mail_accounts");

                migrationBuilder.DropColumn(
                    name: "SettingsVersion",
                    table: "settings_mail_accounts");

                migrationBuilder.DropColumn(
                    name: "SynchronizationMode",
                    table: "settings_mail_accounts");
            }
        }

        /// <summary>The <c>HoldMailGrantsOnUsers</c> migration, as it was added.</summary>
        private static class HoldMailGrantsOnUsers
        {
            // Moves what a caller may do from each credential's list onto its user, without any caller gaining or losing a
            // permission on the day it runs. Before it, a credential's list was the grant, and an unwritten one was written
            // out as the whole mail half on the day the credential was provisioned; after it, a user holds what their role
            // assignments grant and a credential's list only narrows that.
            //
            // So every user who holds no assignment yet is given one at their own scope reproducing the union of what their
            // credentials listed: the seeded Mail user role where the union is the whole mail half, and otherwise a role of
            // its own per distinct union, since a role is the only thing an assignment can name. A user holding no
            // credential is given the whole mail half, because the only caller that could have acted for them is one an
            // endpoint requiring no credential admitted, and that caller held the whole half. A user whose credentials all
            // listed nothing is given nothing. A user who already holds an assignment is left as somebody decided.
            //
            // Every credential listing the whole mail half then records no narrowing at all, which is how a credential
            // provisioned without one was meant: it holds what its user holds, so a permission a later release publishes
            // reaches it once a role carries it. Under the assignment above that is exactly the half it listed, so nothing
            // changes on the day. A credential listing less keeps its list as its narrowing, and holds exactly that list.
            //
            // The mail half is the eleven names as this release publishes them, stated literally rather than read from the
            // build, for the reason the seeding migration gives. The statements share temporary tables and therefore this
            // migration's transaction. Writing roles at all is the one exception to the rule that no migration after the
            // seeding one touches a role, and RoleSeedingMigrationTests names it: it widens no seeded role's list and nobody
            // who already held a role.

            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterColumn<string[]>(
                    name: "Permissions",
                    table: "user_credentials",
                    type: "text[]",
                    nullable: true,
                    oldClrType: typeof(string[]),
                    oldType: "text[]");

                migrationBuilder.Sql(
                    """
                    CREATE TEMPORARY TABLE carried_mail_grants ON COMMIT DROP AS
                    SELECT account."Id" AS user_id,
                           ARRAY(
                               SELECT mail.name
                               FROM unnest(ARRAY[
                        'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                        'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                        'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                        'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write']::text[]) WITH ORDINALITY AS mail(name, position)
                               WHERE NOT EXISTS (SELECT 1 FROM user_credentials held WHERE held."UserId" = account."Id")
                                  OR EXISTS (
                                      SELECT 1 FROM user_credentials held
                                      WHERE held."UserId" = account."Id" AND mail.name = ANY (held."Permissions"))
                               ORDER BY mail.position) AS names
                    FROM settings_accounts account
                    WHERE NOT EXISTS (
                        SELECT 1 FROM role_assignments assigned WHERE assigned."PrincipalUserId" = account."Id");

                    CREATE TEMPORARY TABLE carried_mail_roles ON COMMIT DROP AS
                    SELECT gen_random_uuid() AS role_id,
                           'Carried-over mail grant ' || row_number() OVER (ORDER BY array_to_string(names, ',')) AS role_name,
                           names
                    FROM (
                        SELECT DISTINCT names FROM carried_mail_grants
                        WHERE cardinality(names) BETWEEN 1 AND 10) AS partial_grants;
                    """);

                migrationBuilder.Sql(
                    """
                    INSERT INTO roles ("Id", "Name", "CreatedAt")
                    SELECT role_id, role_name, now() FROM carried_mail_roles;

                    INSERT INTO role_permissions ("RoleId", "Permission")
                    SELECT carried.role_id, name FROM carried_mail_roles carried, unnest(carried.names) AS name;
                    """);

                migrationBuilder.Sql(
                    """
                    INSERT INTO role_assignments ("Id", "RoleId", "PrincipalUserId", "ScopeUserId", "AssignedAt")
                    SELECT gen_random_uuid(), '01a11deb-3808-7000-8000-000000000001'::uuid, carried.user_id, carried.user_id, now()
                    FROM carried_mail_grants carried
                    WHERE cardinality(carried.names) = 11
                    UNION ALL
                    SELECT gen_random_uuid(), carried_role.role_id, carried.user_id, carried.user_id, now()
                    FROM carried_mail_grants carried
                    JOIN carried_mail_roles carried_role ON carried_role.names = carried.names;

                    UPDATE user_credentials SET "Permissions" = NULL
                    WHERE "Permissions" @> ARRAY[
                        'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                        'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                        'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                        'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write']::text[];
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                // A credential naming nothing goes back to listing the whole mail half, which is what it held under the
                // previous release; the roles this migration created go with their assignments. The Mail user assignments it
                // wrote are left, since nothing tells them from ones an operator wrote and the earlier release reads none.
                migrationBuilder.Sql(
                    """
                    UPDATE user_credentials SET "Permissions" = ARRAY[
                        'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                        'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                        'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                        'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write']::text[]
                    WHERE "Permissions" IS NULL;

                    DELETE FROM role_assignments
                    WHERE "RoleId" IN (SELECT "Id" FROM roles WHERE "Name" LIKE 'Carried-over mail grant %');

                    DELETE FROM roles WHERE "Name" LIKE 'Carried-over mail grant %';
                    """);

                migrationBuilder.AlterColumn<string[]>(
                    name: "Permissions",
                    table: "user_credentials",
                    type: "text[]",
                    nullable: false,
                    defaultValue: new string[0],
                    oldClrType: typeof(string[]),
                    oldType: "text[]",
                    oldNullable: true);
            }
        }

        /// <summary>The <c>AdmitUsersOnTheAdministrativeEndpoint</c> migration, as it was added.</summary>
        private static class AdmitUsersOnTheAdministrativeEndpoint
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string[]>(
                    name: "AllowedSourceNetworks",
                    table: "user_credentials",
                    type: "text[]",
                    nullable: false,
                    defaultValueSql: "ARRAY[]::text[]");

                migrationBuilder.AddColumn<string[]>(
                    name: "Surfaces",
                    table: "user_credentials",
                    type: "text[]",
                    nullable: false,
                    defaultValueSql: "ARRAY['mcp','client']::text[]");

                migrationBuilder.CreateTable(
                    name: "default_administrator",
                    columns: table => new
                    {
                        Id = table.Column<short>(type: "smallint", nullable: false),
                        UserId = table.Column<Guid>(type: "uuid", nullable: true),
                        RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        PasswordSettingAppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_default_administrator", x => x.Id);
                        table.CheckConstraint("ck_default_administrator_single_row", "\"Id\" = 1");
                        table.ForeignKey(
                            name: "FK_default_administrator_settings_accounts_UserId",
                            column: x => x.UserId,
                            principalTable: "settings_accounts",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.SetNull);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_default_administrator_UserId",
                    table: "default_administrator",
                    column: "UserId");
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "default_administrator");

                migrationBuilder.DropColumn(
                    name: "AllowedSourceNetworks",
                    table: "user_credentials");

                migrationBuilder.DropColumn(
                    name: "Surfaces",
                    table: "user_credentials");
            }
        }

        /// <summary>The <c>RewriteAdministratorRoleAsAPattern</c> migration, as it was added.</summary>
        /// <remarks>
        /// SQL only and no model change, deliberately: a role's list is rows of one text column, and whether an entry is a
        /// published name or a pattern is decided when a grant is computed, so the schema is the same before and after.
        /// What changes is what the seeded <c>Administrator</c> role holds on a later release: the single pattern
        /// <c>*</c> reaches everything a build publishes, so its holders come to hold a permission added afterwards
        /// without anybody writing to the role.
        /// </remarks>
        private static class RewriteAdministratorRoleAsAPattern
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                // Only the list the seeding migration wrote is rewritten. A role an operator has since narrowed, emptied,
                // or deleted is theirs: rewriting it would hand its holders every permission they were deliberately
                // denied, so it is left exactly as it stands.
                migrationBuilder.Sql(
                    """
                    WITH seeded(name) AS (
                        SELECT unnest(ARRAY[
                            'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                            'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                            'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                            'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write',
                            'mailfathom.admin.read', 'mailfathom.admin.audit.read', 'mailfathom.admin.operate',
                            'mailfathom.admin.credentials.write', 'mailfathom.admin.spend', 'mailfathom.admin.erase',
                            'mailfathom.admin.export', 'mailfathom.admin.configuration.write',
                            'mailfathom.admin.custody.write', 'mailfathom.admin.roles.write'])
                    ),
                    rewritten AS (
                        DELETE FROM role_permissions AS listed
                        WHERE listed."RoleId" = '01a11deb-3808-7000-8000-000000000003'
                          AND NOT EXISTS (
                              SELECT 1 FROM role_permissions AS other
                              WHERE other."RoleId" = listed."RoleId"
                                AND other."Permission" NOT IN (SELECT name FROM seeded))
                          AND (SELECT count(*) FROM role_permissions AS other WHERE other."RoleId" = listed."RoleId")
                              = (SELECT count(*) FROM seeded)
                        RETURNING listed."RoleId"
                    )
                    INSERT INTO role_permissions ("RoleId", "Permission")
                    SELECT DISTINCT rewritten."RoleId", '*' FROM rewritten;
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.Sql(
                    """
                    WITH restored AS (
                        DELETE FROM role_permissions AS listed
                        WHERE listed."RoleId" = '01a11deb-3808-7000-8000-000000000003'
                          AND listed."Permission" = '*'
                          AND NOT EXISTS (
                              SELECT 1 FROM role_permissions AS other
                              WHERE other."RoleId" = listed."RoleId" AND other."Permission" <> '*')
                        RETURNING listed."RoleId"
                    )
                    INSERT INTO role_permissions ("RoleId", "Permission")
                    SELECT restored."RoleId", name FROM restored CROSS JOIN unnest(ARRAY[
                        'mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.contacts.read',
                        'mailfathom.mail.contacts.write', 'mailfathom.mail.flags.write', 'mailfathom.mail.move',
                        'mailfathom.mail.delete', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send',
                        'mailfathom.mail.accounts.write', 'mailfathom.mail.folders.write',
                        'mailfathom.admin.read', 'mailfathom.admin.audit.read', 'mailfathom.admin.operate',
                        'mailfathom.admin.credentials.write', 'mailfathom.admin.spend', 'mailfathom.admin.erase',
                        'mailfathom.admin.export', 'mailfathom.admin.configuration.write',
                        'mailfathom.admin.custody.write', 'mailfathom.admin.roles.write']) AS name;
                    """);
            }
        }

        /// <summary>The <c>AddSettingsPolicies</c> migration, as it was added.</summary>
        private static class AddSettingsPolicies
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "settings_policies",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                        Document = table.Column<string>(type: "jsonb", nullable: false),
                        Version = table.Column<long>(type: "bigint", nullable: false),
                        CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_settings_policies", x => x.Id);
                        table.ForeignKey(
                            name: "fk_settings_policies_organization",
                            column: x => x.OrganizationId,
                            principalTable: "organizations",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "ix_settings_policies_scope",
                    table: "settings_policies",
                    column: "OrganizationId",
                    unique: true)
                    .Annotation("Npgsql:NullsDistinct", false);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(
                    name: "settings_policies");
            }
        }

        /// <summary>The <c>StateEndpointSwitchesHeldOffOnUserRecords</c> migration, as it was added.</summary>
        /// <remarks>
        /// SQL only and no model change, deliberately: the two endpoint columns and the record beside them are the same
        /// before and after. What changes is which of them is the source. A user's row is rewritten from what their record
        /// resolves to by every commit that can move that answer, so a switch a row holds off while the record says
        /// nothing about it would be turned on by the first such commit. The default administrator is the row this
        /// exists for: its switches were written onto the row alone.
        /// </remarks>
        private static class StateEndpointSwitchesHeldOffOnUserRecords
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                // Only a switch that is off is stated, so one that is on stays a setting a policy's default may still
                // supply. A record already carrying the block under any casing has said what it means and is left as it
                // stands, and so is a row whose document is not an object, which no build reads as a record. The version
                // moves because the record did: every replica composes the user again, and an edit composed over the
                // earlier document is refused rather than committed without the block.
                migrationBuilder.Sql(
                    """
                    UPDATE settings_accounts
                    SET "Document" = "Document" || jsonb_build_object(
                            'EndpointAccess',
                            jsonb_strip_nulls(jsonb_build_object(
                                'McpEndpoint', CASE WHEN NOT "McpEndpointEnabled" THEN 'false' END,
                                'ClientEndpoint', CASE WHEN NOT "ClientEndpointEnabled" THEN 'false' END))),
                        "Version" = "Version" + 1
                    WHERE jsonb_typeof("Document") = 'object'
                      AND NOT ("McpEndpointEnabled" AND "ClientEndpointEnabled")
                      AND NOT EXISTS (
                          SELECT 1 FROM jsonb_object_keys("Document") AS stated(key)
                          WHERE lower(stated.key) = 'endpointaccess');
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                // Nothing to take back. The block states what the row already held, an earlier build reads it as it
                // reads any record's, and removing it could not tell a block this wrote from one somebody stated since.
            }
        }

        /// <summary>The <c>StampEndpointSwitchesOnUserRecords</c> migration, as it was added.</summary>
        private static class StampEndpointSwitchesOnUserRecords
        {
            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "EndpointSwitchesOrganizationId",
                    table: "settings_accounts",
                    type: "uuid",
                    nullable: true);

                migrationBuilder.AddColumn<long>(
                    name: "EndpointSwitchesVersion",
                    table: "settings_accounts",
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L);

                // Written by hand, because a default cannot name another column. Every row is stamped as current: the
                // switches a row holds when this runs are what its record resolves to, since no settings policy is stored
                // by a build older than the one that carries this migration. What the default is left to say is the row
                // such a build inserts afterwards, whose switches nothing has derived.
                migrationBuilder.Sql(
                    """
                    UPDATE settings_accounts
                    SET "EndpointSwitchesVersion" = "Version",
                        "EndpointSwitchesOrganizationId" = "OrganizationId";
                    """);
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(
                    name: "EndpointSwitchesOrganizationId",
                    table: "settings_accounts");

                migrationBuilder.DropColumn(
                    name: "EndpointSwitchesVersion",
                    table: "settings_accounts");
            }
        }

        /// <summary>The <c>SplitUserFacingPermissionsByDomain</c> migration, as it was added.</summary>
        /// <remarks>
        /// <para>
        /// SQL only and no model change, deliberately: a permission is stored as its published name, in a role's list and
        /// in the list a credential or a signed-in session is narrowed to, so the schema is the same before and after.
        /// What changes is the names. Asking the assistant and the address book leave the <c>mail</c> domain for domains
        /// of their own, and what <c>mailfathom.mail.read</c> and <c>mailfathom.mail.accounts.write</c> used to reach
        /// beside mail — tasks, the calendar, notifications, and a person's profile — is published under names of its own.
        /// </para>
        /// <para>
        /// Every stored list is carried so that nobody is refused after the upgrade something the list let them do before
        /// it: a renamed name is rewritten where it stands, a list that reached <c>mailfathom.mail.read</c> gains the
        /// eight names split out of it, and one that reached <c>mailfathom.mail.accounts.write</c> gains
        /// <c>mailfathom.profile.write</c>. No other name is written into a list. A list that asked the assistant without
        /// reading mail keeps the asking name alone, because adding the reading one would hand its holder mail they were
        /// never granted.
        /// </para>
        /// <para>
        /// Two lists come out reaching more than they did, and neither is something a rewrite of names can avoid. Changing
        /// a display name is now one act of <c>mailfathom.profile.write</c> among four, so a list that held
        /// <c>mailfathom.mail.accounts.write</c> without the reading name is carried to the whole of it — the alternative
        /// is taking away the one act it had. And a pattern reaches whatever is published under it, as it does for every
        /// name a later release publishes: one written as <c>mailfathom.*.write</c> reaches the new writing names whether
        /// or not its role read mail.
        /// </para>
        /// <para>
        /// A role may list a pattern, and a pattern is carried by what it reached rather than by how it is written: the
        /// names a role held through one are written out wherever its rewritten list no longer reaches them, so
        /// <c>mailfathom.mail.*</c> goes on meaning what it meant, and a role listing <c>*</c> gains no row at all.
        /// </para>
        /// </remarks>
        private static class SplitUserFacingPermissionsByDomain
        {
            // What a list holding the name on the left is carried to. A name that is renamed appears once; a name that is
            // split appears once per name split out of it, and stays in the list itself.
            private const string CarriedNames =
                """
                ('mailfathom.mail.ask', 'mailfathom.assistant.use'),
                ('mailfathom.mail.contacts.read', 'mailfathom.contacts.read'),
                ('mailfathom.mail.contacts.write', 'mailfathom.contacts.write'),
                ('mailfathom.mail.read', 'mailfathom.tasks.read'),
                ('mailfathom.mail.read', 'mailfathom.tasks.write'),
                ('mailfathom.mail.read', 'mailfathom.calendar.read'),
                ('mailfathom.mail.read', 'mailfathom.calendar.write'),
                ('mailfathom.mail.read', 'mailfathom.notifications.read'),
                ('mailfathom.mail.read', 'mailfathom.notifications.write'),
                ('mailfathom.mail.read', 'mailfathom.profile.read'),
                ('mailfathom.mail.read', 'mailfathom.profile.write'),
                ('mailfathom.mail.accounts.write', 'mailfathom.profile.write')
                """;

            private const string SplitOutNames =
                """
                'mailfathom.tasks.read', 'mailfathom.tasks.write', 'mailfathom.calendar.read',
                'mailfathom.calendar.write', 'mailfathom.notifications.read', 'mailfathom.notifications.write',
                'mailfathom.profile.read', 'mailfathom.profile.write'
                """;

            /// <summary>Applies the migration.</summary>
            internal static void Up(MigrationBuilder migrationBuilder)
            {
                // One statement, so that what each role held is read from the lists as they stood before any of them was
                // rewritten. A pattern is matched the way a grant is computed from one: a wildcard is a whole segment
                // standing for one or more, which is the expression each pattern is turned into below. An entry that is
                // neither a name nor a pattern is matched against nothing, exactly as it grants nothing — and it is given
                // no expression at all rather than one a condition is trusted to skip, because the order two halves of a
                // condition are evaluated in is the planner's, and an entry nobody validated need not be an expression.
                migrationBuilder.Sql(
                    $$"""
                    WITH carried(old_name, new_name) AS (
                        VALUES
                        {{CarriedNames}}
                    ),
                    listed AS (
                        SELECT entry."RoleId" AS role_id,
                               entry."Permission" AS written,
                               CASE
                                   WHEN entry."Permission" = 'mailfathom.mail.ask' THEN 'mailfathom.assistant.use'
                                   WHEN entry."Permission" LIKE 'mailfathom.mail.contacts.%'
                                       THEN 'mailfathom.contacts.' || substr(entry."Permission", 26)
                                   ELSE entry."Permission"
                               END AS rewritten
                        FROM role_permissions AS entry
                    ),
                    shaped AS (
                        SELECT listed.role_id,
                               listed.written,
                               listed.rewritten,
                               listed.written ~ '^(\*|[a-z0-9_]+)(\.(\*|[a-z0-9_]+))*$' AND listed.written LIKE '%*%' AS is_pattern
                        FROM listed
                    ),
                    matched AS (
                        SELECT shaped.role_id,
                               shaped.written,
                               shaped.rewritten,
                               shaped.is_pattern,
                               CASE WHEN shaped.is_pattern
                                   THEN '^' || replace(replace(shaped.written, '.', '\.'), '*', '[^.]+(\.[^.]+)*') || '$'
                               END AS reaches_before,
                               CASE WHEN shaped.is_pattern
                                   THEN '^' || replace(replace(shaped.rewritten, '.', '\.'), '*', '[^.]+(\.[^.]+)*') || '$'
                               END AS reaches_after
                        FROM shaped
                    ),
                    held AS (
                        SELECT DISTINCT matched.role_id, carried.new_name
                        FROM matched
                        JOIN carried
                          ON matched.written = carried.old_name
                          OR (matched.is_pattern AND carried.old_name ~ matched.reaches_before)
                    ),
                    renamed AS (
                        UPDATE role_permissions AS entry
                        SET "Permission" = listed.rewritten
                        FROM listed
                        WHERE entry."RoleId" = listed.role_id
                          AND entry."Permission" = listed.written
                          AND listed.written <> listed.rewritten
                    )
                    INSERT INTO role_permissions ("RoleId", "Permission")
                    SELECT held.role_id, held.new_name
                    FROM held
                    WHERE NOT EXISTS (
                        SELECT 1 FROM matched
                        WHERE matched.role_id = held.role_id
                          AND (matched.rewritten = held.new_name
                               OR (matched.is_pattern AND held.new_name ~ matched.reaches_after)))
                    ON CONFLICT DO NOTHING;
                    """);

                migrationBuilder.Sql(CarryingANarrowingForward("user_credentials"));
                migrationBuilder.Sql(CarryingANarrowingForward("client_sessions"));
            }

            /// <summary>Reverts the migration.</summary>
            internal static void Down(MigrationBuilder migrationBuilder)
            {
                // The build this returns to publishes none of the split-out names, so every one of them leaves whoever
                // lists it, and the two renames are undone where they stand.
                migrationBuilder.Sql(
                    $$"""
                    DELETE FROM role_permissions WHERE "Permission" IN ({{SplitOutNames}});

                    UPDATE role_permissions
                    SET "Permission" = CASE
                            WHEN "Permission" = 'mailfathom.assistant.use' THEN 'mailfathom.mail.ask'
                            ELSE 'mailfathom.mail.contacts.' || substr("Permission", 21)
                        END
                    WHERE "Permission" = 'mailfathom.assistant.use' OR "Permission" LIKE 'mailfathom.contacts.%';
                    """);

                migrationBuilder.Sql(CarryingANarrowingBack("user_credentials"));
                migrationBuilder.Sql(CarryingANarrowingBack("client_sessions"));
            }

            // A narrowing names permissions exactly and never a pattern, so it is carried by equality alone. A credential
            // narrowed to nothing in particular holds no list and is left without one.
            private static string CarryingANarrowingForward(string table) =>
                $$"""
                WITH carried(old_name, new_name) AS (
                    VALUES
                    {{CarriedNames}}
                )
                UPDATE {{table}} AS narrowed
                SET "Permissions" = ARRAY(
                    SELECT DISTINCT name
                    FROM (
                        SELECT listed AS name
                        FROM unnest(narrowed."Permissions") AS listed
                        WHERE listed NOT IN (
                            'mailfathom.mail.ask', 'mailfathom.mail.contacts.read', 'mailfathom.mail.contacts.write')
                        UNION ALL
                        SELECT carried.new_name FROM carried WHERE carried.old_name = ANY (narrowed."Permissions")
                    ) AS names
                    ORDER BY name)
                WHERE narrowed."Permissions" && ARRAY(SELECT carried.old_name FROM carried);
                """;

            private static string CarryingANarrowingBack(string table) =>
                $$"""
                UPDATE {{table}} AS narrowed
                SET "Permissions" = ARRAY(
                    SELECT DISTINCT CASE
                               WHEN listed = 'mailfathom.assistant.use' THEN 'mailfathom.mail.ask'
                               WHEN listed LIKE 'mailfathom.contacts.%' THEN 'mailfathom.mail.contacts.' || substr(listed, 21)
                               ELSE listed
                           END AS name
                    FROM unnest(narrowed."Permissions") AS listed
                    WHERE listed NOT IN ({{SplitOutNames}})
                    ORDER BY name)
                WHERE narrowed."Permissions" && ARRAY[
                    'mailfathom.assistant.use', 'mailfathom.contacts.read', 'mailfathom.contacts.write',
                    {{SplitOutNames}}];
                """;
        }
    }
}
