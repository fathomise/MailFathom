// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ForgetWhatWasDerivedFromUnsentMail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mail the mailbox has not sent — a draft, or a send waiting in or refused by the outbox — is no longer read
            // by any derived work, so what an earlier release derived from it is removed rather than left to be served:
            // its passages, which take their vectors in cascade; its readings, which take their marks; the dates read out
            // of it that nobody accepted, which take their reminders; and the state of every conversation it counted
            // towards, which the next pass derives again without it. An event the person accepted is theirs and stays.
            // Unsent is recognized as the stores recognize it: the message carries \Draft, or sits in the folder its
            // account maps as drafts. The model does not change, and a replica of the previous release still running
            // may derive from such mail again until it is replaced, which the next pass of this build does not undo.
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE mf_unsent_stored_emails ON COMMIT DROP AS
                SELECT email."Id", email."EmailThreadId"
                FROM stored_emails AS email
                LEFT JOIN local_mail_folders AS folder ON folder."Id" = email."FolderId"
                WHERE email."IsRemotelyDraft" OR folder."Role" = 'Drafts';

                DELETE FROM email_chunks WHERE "StoredEmailId" IN (SELECT "Id" FROM mf_unsent_stored_emails);

                DELETE FROM email_enrichments WHERE "StoredEmailId" IN (SELECT "Id" FROM mf_unsent_stored_emails);

                DELETE FROM calendar_events
                WHERE "Origin" = 'Proposed' AND "SourceStoredEmailId" IN (SELECT "Id" FROM mf_unsent_stored_emails);

                DELETE FROM email_thread_states
                WHERE "EmailThreadId" IN (SELECT "EmailThreadId" FROM mf_unsent_stored_emails WHERE "EmailThreadId" IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // What was removed was derived, and the build this reverts to derives it again on its next pass.
        }
    }
}
