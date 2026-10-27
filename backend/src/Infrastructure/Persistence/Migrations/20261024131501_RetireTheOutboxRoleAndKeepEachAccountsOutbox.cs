// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireTheOutboxRoleAndKeepEachAccountsOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DismissedAt",
                table: "outgoing_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DismissedByUserId",
                table: "outgoing_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "outgoing_emails",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_emails_outbox",
                table: "outgoing_emails",
                columns: new[] { "MailboxAccountId", "RecordedAt", "Id" },
                filter: "\"DismissedAt\" IS NULL AND \"Stage\" IN ('Recorded', 'TransmissionBegun', 'Refused')");

            // No folder plays the outbox any more, so nothing an earlier release recorded about one may outlive it. A
            // held filing described a copy mirrored into that folder; the row goes and the copy stays on the server as
            // an ordinary message, because withdrawing it is an act against somebody's mailbox that nothing asked for.
            // The role is cleared from the folder rows and from the relational projection of each account's settings,
            // and from the account documents themselves, which the host binds with the role refused: a document still
            // naming it would stop the next start. A document is read the way the host binds one — property names in
            // any case, the folder list as an array or as an object keyed by position, the role written by name or by
            // its retired ordinal — and its version moves on, as any write of it does, so a cached reading is retired.
            migrationBuilder.Sql(
                """
                DELETE FROM outgoing_email_filings WHERE "Filing" = 'held';

                UPDATE local_mail_folders SET "Role" = NULL WHERE "Role" = 'Outbox';

                UPDATE mail_account_folder_settings SET "SpecialUse" = NULL WHERE "SpecialUse" = 9;

                CREATE FUNCTION pg_temp.mf_names_outbox_role(entry jsonb) RETURNS boolean
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT jsonb_typeof(entry) = 'object' AND EXISTS (
                        SELECT 1
                        FROM jsonb_each(entry) AS member
                        WHERE lower(member.key) = 'specialuse'
                          AND jsonb_typeof(member.value) IN ('string', 'number')
                          AND lower(btrim(member.value #>> '{}')) IN ('outbox', '9'))
                $$;

                CREATE FUNCTION pg_temp.mf_without_outbox_role(entry jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN pg_temp.mf_names_outbox_role(entry) THEN (
                        SELECT coalesce(jsonb_object_agg(member.key, member.value), '{}'::jsonb)
                        FROM jsonb_each(entry) AS member
                        WHERE lower(member.key) <> 'specialuse')
                    ELSE entry END
                $$;

                CREATE FUNCTION pg_temp.mf_folder_entries(folders jsonb) RETURNS SETOF jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT value FROM jsonb_array_elements(CASE jsonb_typeof(folders) WHEN 'array' THEN folders ELSE '[]'::jsonb END)
                    UNION ALL
                    SELECT value FROM jsonb_each(CASE jsonb_typeof(folders) WHEN 'object' THEN folders ELSE '{}'::jsonb END)
                $$;

                CREATE FUNCTION pg_temp.mf_folders_without_outbox_role(folders jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE jsonb_typeof(folders)
                        WHEN 'array' THEN (
                            SELECT coalesce(jsonb_agg(pg_temp.mf_without_outbox_role(entry.value) ORDER BY entry.position), '[]'::jsonb)
                            FROM jsonb_array_elements(folders) WITH ORDINALITY AS entry(value, position))
                        WHEN 'object' THEN (
                            SELECT coalesce(jsonb_object_agg(entry.key, pg_temp.mf_without_outbox_role(entry.value)), '{}'::jsonb)
                            FROM jsonb_each(folders) AS entry)
                        ELSE folders
                    END
                $$;

                UPDATE settings_mail_accounts AS account
                SET "Document" = (
                        SELECT jsonb_object_agg(
                            member.key,
                            CASE WHEN lower(member.key) = 'folders'
                                THEN pg_temp.mf_folders_without_outbox_role(member.value)
                                ELSE member.value
                            END)
                        FROM jsonb_each(account."Document") AS member),
                    "Version" = account."Version" + 1
                WHERE jsonb_typeof(account."Document") = 'object'
                  AND EXISTS (
                      SELECT 1
                      FROM jsonb_each(account."Document") AS member
                      CROSS JOIN LATERAL pg_temp.mf_folder_entries(member.value) AS folder(entry)
                      WHERE lower(member.key) = 'folders'
                        AND pg_temp.mf_names_outbox_role(folder.entry));

                DROP FUNCTION pg_temp.mf_folders_without_outbox_role(jsonb);
                DROP FUNCTION pg_temp.mf_folder_entries(jsonb);
                DROP FUNCTION pg_temp.mf_without_outbox_role(jsonb);
                DROP FUNCTION pg_temp.mf_names_outbox_role(jsonb);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outgoing_emails_outbox",
                table: "outgoing_emails");

            migrationBuilder.DropColumn(
                name: "DismissedAt",
                table: "outgoing_emails");

            migrationBuilder.DropColumn(
                name: "DismissedByUserId",
                table: "outgoing_emails");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "outgoing_emails");
        }
    }
}
