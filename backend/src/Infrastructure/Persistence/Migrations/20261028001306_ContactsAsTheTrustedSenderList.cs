// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContactsAsTheTrustedSenderList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SenderTrustAddress",
                table: "stored_emails",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderTrustAuthorEvidence",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<Guid>(
                name: "SenderTrustContactId",
                table: "stored_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WriterCredentialId",
                table: "contact_addresses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WriterKind",
                table: "contact_addresses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Unrecorded");

            migrationBuilder.AddColumn<Guid>(
                name: "WriterUserId",
                table: "contact_addresses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WrittenAt",
                table: "contact_addresses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_sender_trust_address",
                table: "stored_emails",
                columns: new[] { "MailboxAccountId", "SenderTrustAddress" },
                filter: "\"SenderTrustAddress\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stored_emails_sender_trust_contact",
                table: "stored_emails",
                column: "SenderTrustContactId",
                filter: "\"SenderTrustContactId\" IS NOT NULL");

            // The configured and stored trusted-sender lists are gone, so a verdict either of them reached names a source
            // that no longer exists. It becomes the not-evaluated verdict, which a rederive judges again.
            migrationBuilder.Sql(
                """
                UPDATE stored_emails
                SET "SenderTrustLevel" = 'Unknown',
                    "SenderTrustGrantedBy" = 'None',
                    "SenderTrustPolicyRevision" = NULL
                WHERE "SenderTrustGrantedBy" IN ('ConfiguredTrustedSender', 'StoredTrustedSender');
                """);

            // A mail account record and a stored settings policy are both judged strictly on every read, so one still
            // naming the retired setting would be refused: the account left unserved, the whole policy ignored. The
            // setting is removed from an account record, from a policy's MailAccounts defaults and forced values, and
            // from the paths its editing restriction lists — in any casing, because keys are read case-insensitively.
            // Not reversed by Down: the entries are what an operator re-creates as contacts.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION pg_temp.without_trusted_senders(account jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN jsonb_typeof(account) <> 'object' THEN account ELSE COALESCE(
                        (SELECT jsonb_object_agg(key, value) FROM jsonb_each(account)
                         WHERE lower(key) <> 'trustedsenders'),
                        '{}'::jsonb) END
                $$;

                CREATE FUNCTION pg_temp.without_trusted_sender_paths(editing jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN jsonb_typeof(editing) <> 'object' THEN editing ELSE COALESCE(
                        (SELECT jsonb_object_agg(key, CASE
                            WHEN lower(key) = 'properties' AND jsonb_typeof(value) = 'array' THEN COALESCE(
                                (SELECT jsonb_agg(path ORDER BY position)
                                 FROM jsonb_array_elements(value) WITH ORDINALITY AS listed(path, position)
                                 WHERE jsonb_typeof(path) <> 'string'
                                    OR (lower(path #>> '{}') <> 'trustedsenders'
                                        AND lower(path #>> '{}') NOT LIKE 'trustedsenders:%')),
                                '[]'::jsonb)
                            ELSE value END)
                         FROM jsonb_each(editing)),
                        '{}'::jsonb) END
                $$;

                CREATE FUNCTION pg_temp.without_trusted_senders_in_policy(policy jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN jsonb_typeof(policy) <> 'object' THEN policy ELSE COALESCE(
                        (SELECT jsonb_object_agg(section_key, CASE
                            WHEN lower(section_key) = 'mailaccounts' AND jsonb_typeof(section) = 'object' THEN COALESCE(
                                (SELECT jsonb_object_agg(key, CASE
                                    WHEN lower(key) IN ('defaults', 'forced') THEN pg_temp.without_trusted_senders(value)
                                    WHEN lower(key) = 'editing' THEN pg_temp.without_trusted_sender_paths(value)
                                    ELSE value END)
                                 FROM jsonb_each(section)),
                                '{}'::jsonb)
                            ELSE section END)
                         FROM jsonb_each(policy) AS stated(section_key, section)),
                        '{}'::jsonb) END
                $$;

                UPDATE settings_mail_accounts
                SET "Document" = pg_temp.without_trusted_senders("Document"),
                    "Version" = "Version" + 1,
                    "UpdatedAt" = now()
                WHERE pg_temp.without_trusted_senders("Document") <> "Document";

                UPDATE settings_policies
                SET "Document" = pg_temp.without_trusted_senders_in_policy("Document"),
                    "Version" = "Version" + 1,
                    "UpdatedAt" = now()
                WHERE pg_temp.without_trusted_senders_in_policy("Document") <> "Document";

                DROP FUNCTION pg_temp.without_trusted_senders_in_policy(jsonb);
                DROP FUNCTION pg_temp.without_trusted_sender_paths(jsonb);
                DROP FUNCTION pg_temp.without_trusted_senders(jsonb);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stored_emails_sender_trust_address",
                table: "stored_emails");

            migrationBuilder.DropIndex(
                name: "ix_stored_emails_sender_trust_contact",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SenderTrustAddress",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SenderTrustAuthorEvidence",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SenderTrustContactId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "WriterCredentialId",
                table: "contact_addresses");

            migrationBuilder.DropColumn(
                name: "WriterKind",
                table: "contact_addresses");

            migrationBuilder.DropColumn(
                name: "WriterUserId",
                table: "contact_addresses");

            migrationBuilder.DropColumn(
                name: "WrittenAt",
                table: "contact_addresses");
        }
    }
}
