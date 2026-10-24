// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// The expanding half of ADR 0039's schema change: every account gets folder rows, every binding names its folder,
    /// and every stored message names exactly one folder through <c>FolderId</c>. The columns a stored message named its
    /// folder by until now — the binding and, on a held account, the local folder — stay and are still written, and
    /// triggers carry what an older build writes into them onto the new references, so two builds against one database
    /// never disagree about which folder a message is in. A later release drops those columns and the triggers.
    /// </remarks>
    public partial class GiveEveryMailAccountFolderRows : Migration
    {
        /// <summary>The roles in the order <c>MailFolderSpecialUse</c> numbers them, which is how folder settings store one.</summary>
        private const string RolesByNumber =
            "ARRAY['Inbox','Archive','Drafts','Sent','Junk','Trash','All','Flagged','Important','Outbox']";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "mail_folders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_mail_folders_account_alias",
                table: "local_mail_folders",
                columns: new[] { "MailboxAccountId", "SourceFolderAlias" },
                unique: true,
                filter: "\"SourceFolderAlias\" IS NOT NULL AND \"ErasedAt\" IS NULL");

            // A version 7 identifier minted in SQL, as ADR 0036 requires of a migration that mints a row: the first six
            // bytes of a random version 4 value are overwritten with the millisecond clock and its version nibble is
            // raised from 4 to 7, which leaves the variant bits as they were. Written out rather than PostgreSQL 18's
            // uuidv7() because a deployment may still run PostgreSQL 17.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION mailfathom_uuid_v7() RETURNS uuid
                LANGUAGE sql VOLATILE AS $$
                    SELECT encode(
                        set_bit(set_bit(
                            overlay(uuid_send(gen_random_uuid())
                                placing substring(int8send(floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
                                FROM 1 FOR 6),
                            52, 1), 53, 1),
                        'hex')::uuid
                $$;
                """);

            // Every alias an account binds or declares gets a live folder carrying it, so every binding and every stored
            // message has a folder to name. Mirrored accounts get their first rows here, flat and named after the source
            // folder; synchronization projects their parents and names on its next run. A held account keeps the folders
            // it already has: a folder playing the role a declaration plays adopts that alias, and an alias it never
            // placed mail from gets the folder its next arrival would have created. An alias whose held folder was erased
            // gets nothing, because arrivals from it go to the inbox.
            migrationBuilder.Sql(
                $$"""
                DO $backfill$
                DECLARE
                    candidate RECORD;
                    folder_name text;
                    folder_role text;
                BEGIN
                    FOR candidate IN
                        WITH newest AS (
                            SELECT DISTINCT ON (binding."MailboxAccountId", binding."Alias")
                                   binding."MailboxAccountId" AS account_id, binding."Alias" AS alias,
                                   binding."RemotePath" AS remote_path, binding."HierarchyDelimiter" AS delimiter
                            FROM mail_folders AS binding
                            ORDER BY binding."MailboxAccountId", binding."Alias", binding."ResolutionGeneration" DESC
                        ),
                        declared AS (
                            SELECT settings."MailAccountId"::text AS account_id, settings."Alias" AS alias,
                                   ({{RolesByNumber}})[settings."SpecialUse" + 1] AS role
                            FROM mail_account_folder_settings AS settings
                            WHERE EXISTS (
                                SELECT 1 FROM mailbox_accounts AS account WHERE account."Id" = settings."MailAccountId"::text)
                        )
                        SELECT coalesce(newest.account_id, declared.account_id) AS account_id,
                               coalesce(newest.alias, declared.alias) AS alias,
                               newest.remote_path, newest.delimiter, declared.role
                        FROM newest
                        FULL JOIN declared ON declared.account_id = newest.account_id AND declared.alias = newest.alias
                        ORDER BY 1, 2
                    LOOP
                        CONTINUE WHEN EXISTS (
                            SELECT 1 FROM local_mail_folders AS folder
                            WHERE folder."MailboxAccountId" = candidate.account_id
                              AND folder."SourceFolderAlias" = candidate.alias);

                        IF candidate.role IS NOT NULL THEN
                            UPDATE local_mail_folders AS folder
                            SET "SourceFolderAlias" = candidate.alias
                            WHERE folder."MailboxAccountId" = candidate.account_id
                              AND folder."Role" = candidate.role
                              AND folder."ErasedAt" IS NULL
                              AND folder."SourceFolderAlias" IS NULL;

                            CONTINUE WHEN FOUND;
                        END IF;

                        folder_name := btrim(CASE
                            WHEN candidate.remote_path IS NULL THEN candidate.alias
                            WHEN coalesce(candidate.delimiter, '') = '' THEN candidate.remote_path
                            ELSE reverse(split_part(reverse(candidate.remote_path), candidate.delimiter, 1))
                        END);

                        IF folder_name = '' OR length(folder_name) > 255 OR folder_name ~ '[[:cntrl:]/]' THEN
                            folder_name := candidate.alias;
                        END IF;

                        IF EXISTS (
                            SELECT 1 FROM local_mail_folders AS sibling
                            WHERE sibling."MailboxAccountId" = candidate.account_id
                              AND sibling."ParentId" IS NULL
                              AND sibling."ErasedAt" IS NULL
                              AND sibling."NameKey" = upper(folder_name)) THEN
                            folder_name := left(folder_name, 120) || ' (' || candidate.alias || ')';
                        END IF;

                        folder_role := CASE
                            WHEN candidate.role IS NOT NULL AND NOT EXISTS (
                                SELECT 1 FROM local_mail_folders AS other
                                WHERE other."MailboxAccountId" = candidate.account_id
                                  AND other."Role" = candidate.role
                                  AND other."ErasedAt" IS NULL)
                            THEN candidate.role
                        END;

                        INSERT INTO local_mail_folders
                            ("Id", "MailboxAccountId", "ParentId", "Name", "NameKey", "Role", "SourceFolderAlias", "ErasedAt")
                        VALUES
                            (mailfathom_uuid_v7(), candidate.account_id, NULL, folder_name, upper(folder_name),
                             folder_role, candidate.alias, NULL);
                    END LOOP;
                END
                $backfill$;
                """);

            // A binding names the live folder carrying its alias. Where that folder was erased it names none, which is
            // how a held account's placement already reads an erased source: its arrivals go to the inbox.
            migrationBuilder.Sql(
                """
                UPDATE mail_folders AS binding
                SET "FolderId" = folder."Id"
                FROM local_mail_folders AS folder
                WHERE folder."MailboxAccountId" = binding."MailboxAccountId"
                  AND folder."SourceFolderAlias" = binding."Alias"
                  AND folder."ErasedAt" IS NULL;
                """);

            // An account left with mail and no folder for it — held, its source folder erased, and nothing yet having
            // supplied its protected folders — gets an inbox, so the statement below never leaves a message unplaced.
            migrationBuilder.Sql(
                """
                INSERT INTO local_mail_folders
                    ("Id", "MailboxAccountId", "ParentId", "Name", "NameKey", "Role", "SourceFolderAlias", "ErasedAt")
                SELECT mailfathom_uuid_v7(), unplaced."MailboxAccountId", NULL, 'INBOX', 'INBOX', 'Inbox', NULL, NULL
                FROM (
                    SELECT DISTINCT email."MailboxAccountId"
                    FROM stored_emails AS email
                    JOIN mail_folders AS binding ON binding."Id" = email."MailFolderId"
                    WHERE email."LocalMailFolderId" IS NULL AND binding."FolderId" IS NULL
                ) AS unplaced
                WHERE NOT EXISTS (
                    SELECT 1 FROM local_mail_folders AS inbox
                    WHERE inbox."MailboxAccountId" = unplaced."MailboxAccountId"
                      AND inbox."ErasedAt" IS NULL
                      AND (inbox."Role" = 'Inbox' OR (inbox."ParentId" IS NULL AND inbox."NameKey" = 'INBOX')));
                """);

            // Every stored message is written once here, which is what makes this migration take time in proportion to
            // the stored mail. A message keeps the local folder a held account placed it in; any other is in the folder
            // its binding binds, or in the inbox where that folder was erased.
            migrationBuilder.Sql(
                """
                UPDATE stored_emails AS email
                SET "FolderId" = coalesce(
                    email."LocalMailFolderId",
                    binding."FolderId",
                    (SELECT inbox."Id" FROM local_mail_folders AS inbox
                     WHERE inbox."MailboxAccountId" = email."MailboxAccountId"
                       AND inbox."ErasedAt" IS NULL
                       AND (inbox."Role" = 'Inbox' OR (inbox."ParentId" IS NULL AND inbox."NameKey" = 'INBOX'))
                     ORDER BY inbox."Role" NULLS LAST
                     LIMIT 1))
                FROM mail_folders AS binding
                WHERE binding."Id" = email."MailFolderId";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "FolderId",
                table: "stored_emails",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_folder_reference_timeline",
                table: "stored_emails",
                columns: new[] { "FolderId", "ReceivedAt", "Id" },
                descending: new[] { false, true, true })
                .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified });

            migrationBuilder.CreateIndex(
                name: "IX_mail_folders_FolderId",
                table: "mail_folders",
                column: "FolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_mail_folders_local_mail_folders_FolderId",
                table: "mail_folders",
                column: "FolderId",
                principalTable: "local_mail_folders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_stored_emails_local_mail_folders_FolderId",
                table: "stored_emails",
                column: "FolderId",
                principalTable: "local_mail_folders",
                principalColumn: "Id");

            CarryWhatAnOlderBuildWrites(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER carry_stored_email_folder ON stored_emails;
                DROP FUNCTION mailfathom_carry_stored_email_folder();
                DROP TRIGGER carry_binding_folder ON mail_folders;
                DROP FUNCTION mailfathom_carry_binding_folder();
                DROP FUNCTION mailfathom_uuid_v7();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_mail_folders_local_mail_folders_FolderId",
                table: "mail_folders");

            migrationBuilder.DropForeignKey(
                name: "FK_stored_emails_local_mail_folders_FolderId",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_folder_reference_timeline",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "IX_mail_folders_FolderId",
                table: "mail_folders");

            migrationBuilder.DropIndex(
                name: "ix_local_mail_folders_account_alias",
                table: "local_mail_folders");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "mail_folders");
        }

        /// <summary>
        /// Keeps the new references right while an older build still writes only the old ones, for as long as a rolling
        /// upgrade runs both builds; the release that drops the old columns drops these triggers with them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A binding an older build inserts names the live folder carrying its alias. A message an older build writes
        /// takes its folder from the local folder wherever that is what the write changed, which is what a held
        /// account's placement and local moves write; otherwise from its binding's folder, so a relocation the server
        /// confirmed moves it even on an account a restore left carrying the local folder it had while held.
        /// </para>
        /// <para>
        /// An older build knows nothing of folder rows, so a mirrored account's newly declared folder has none when its
        /// first message arrives: the trigger creates it there, named after the alias until synchronization projects its
        /// name. A held account's arrival with no folder yet goes into the inbox for the moment, because the older build
        /// places it in its local folder within the same transaction, which the local branch then carries.
        /// </para>
        /// <para>
        /// The new build writes both references itself and writes them equal, so for it the triggers change nothing.
        /// </para>
        /// </remarks>
        private static void CarryWhatAnOlderBuildWrites(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION mailfathom_carry_binding_folder() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."FolderId" IS NULL THEN
                        SELECT folder."Id" INTO NEW."FolderId"
                        FROM local_mail_folders AS folder
                        WHERE folder."MailboxAccountId" = NEW."MailboxAccountId"
                          AND folder."SourceFolderAlias" = NEW."Alias"
                          AND folder."ErasedAt" IS NULL;
                    END IF;

                    RETURN NEW;
                END
                $$;

                CREATE TRIGGER carry_binding_folder
                    BEFORE INSERT ON mail_folders
                    FOR EACH ROW EXECUTE FUNCTION mailfathom_carry_binding_folder();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION mailfathom_carry_stored_email_folder() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    binding_folder uuid;
                    binding_alias text;
                    account_phase text;
                    folder_name text;
                BEGIN
                    SELECT binding."FolderId", binding."Alias" INTO binding_folder, binding_alias
                    FROM mail_folders AS binding
                    WHERE binding."Id" = NEW."MailFolderId";

                    IF TG_OP = 'UPDATE' THEN
                        IF NEW."LocalMailFolderId" IS NOT NULL
                           AND NEW."LocalMailFolderId" IS DISTINCT FROM OLD."LocalMailFolderId" THEN
                            NEW."FolderId" := NEW."LocalMailFolderId";
                        ELSIF NEW."MailFolderId" IS DISTINCT FROM OLD."MailFolderId" THEN
                            NEW."FolderId" := coalesce(binding_folder, NEW."FolderId");
                        END IF;

                        RETURN NEW;
                    END IF;

                    IF NEW."LocalMailFolderId" IS NOT NULL THEN
                        NEW."FolderId" := NEW."LocalMailFolderId";

                        RETURN NEW;
                    END IF;

                    IF NEW."FolderId" IS NOT NULL THEN
                        RETURN NEW;
                    END IF;

                    NEW."FolderId" := coalesce(binding_folder, (
                        SELECT folder."Id" FROM local_mail_folders AS folder
                        WHERE folder."MailboxAccountId" = NEW."MailboxAccountId"
                          AND folder."SourceFolderAlias" = binding_alias
                          AND folder."ErasedAt" IS NULL));

                    IF NEW."FolderId" IS NOT NULL THEN
                        RETURN NEW;
                    END IF;

                    SELECT account."CustodyPhase" INTO account_phase
                    FROM mailbox_accounts AS account
                    WHERE account."Id" = NEW."MailboxAccountId";

                    IF account_phase = 'Mirrored' THEN
                        folder_name := binding_alias;

                        IF EXISTS (
                            SELECT 1 FROM local_mail_folders AS sibling
                            WHERE sibling."MailboxAccountId" = NEW."MailboxAccountId"
                              AND sibling."ParentId" IS NULL
                              AND sibling."ErasedAt" IS NULL
                              AND sibling."NameKey" = upper(folder_name)) THEN
                            folder_name := binding_alias || ' (' || NEW."MailFolderId" || ')';
                        END IF;

                        NEW."FolderId" := mailfathom_uuid_v7();

                        INSERT INTO local_mail_folders
                            ("Id", "MailboxAccountId", "ParentId", "Name", "NameKey", "Role", "SourceFolderAlias", "ErasedAt")
                        VALUES
                            (NEW."FolderId", NEW."MailboxAccountId", NULL, folder_name, upper(folder_name), NULL,
                             binding_alias, NULL);

                        UPDATE mail_folders AS binding
                        SET "FolderId" = NEW."FolderId"
                        WHERE binding."MailboxAccountId" = NEW."MailboxAccountId"
                          AND binding."Alias" = binding_alias
                          AND binding."FolderId" IS NULL;
                    ELSE
                        SELECT inbox."Id" INTO NEW."FolderId"
                        FROM local_mail_folders AS inbox
                        WHERE inbox."MailboxAccountId" = NEW."MailboxAccountId"
                          AND inbox."Role" = 'Inbox'
                          AND inbox."ErasedAt" IS NULL;
                    END IF;

                    RETURN NEW;
                END
                $$;

                CREATE TRIGGER carry_stored_email_folder
                    BEFORE INSERT OR UPDATE OF "LocalMailFolderId", "MailFolderId" ON stored_emails
                    FOR EACH ROW EXECUTE FUNCTION mailfathom_carry_stored_email_folder();
                """);
        }
    }
}
