// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSignatureVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureAuthorityCheck",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Skipped");

            migrationBuilder.AddColumn<string>(
                name: "SignatureChainEnd",
                table: "stored_emails",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureContentCheck",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Skipped");

            migrationBuilder.AddColumn<string>(
                name: "SignatureFormat",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureOutcome",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "NotSigned");

            migrationBuilder.AddColumn<string>(
                name: "SignatureRevocationCheck",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Skipped");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignatureRevocationCheckedAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureRevocationMethod",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignatureRevokedAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureSecureMimeType",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignatureSignedAt",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureSignerCheck",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Skipped");

            migrationBuilder.AddColumn<string>(
                name: "SignatureTrustAnchor",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureUnverifiableReason",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureValidityCheck",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Skipped");

            migrationBuilder.AddColumn<string>(
                name: "SignatureValidityFailure",
                table: "stored_emails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerAddress",
                table: "stored_emails",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerDigestAlgorithm",
                table: "stored_emails",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerFingerprint",
                table: "stored_emails",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerIssuer",
                table: "stored_emails",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerKeyId",
                table: "stored_emails",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerName",
                table: "stored_emails",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerPublicKeyAlgorithm",
                table: "stored_emails",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerSerialNumber",
                table: "stored_emails",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignerValidFrom",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignerValidUntil",
                table: "stored_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stored_email_signer_keys",
                columns: table => new
                {
                    StoredEmailId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyMaterial = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_email_signer_keys", x => x.StoredEmailId);
                    table.ForeignKey(
                        name: "FK_stored_email_signer_keys_stored_emails_StoredEmailId",
                        column: x => x.StoredEmailId,
                        principalTable: "stored_emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_email_signer_keys");

            migrationBuilder.DropColumn(
                name: "SignatureAuthorityCheck",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureChainEnd",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureContentCheck",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureFormat",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureOutcome",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureRevocationCheck",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureRevocationCheckedAt",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureRevocationMethod",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureRevokedAt",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureSecureMimeType",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureSignedAt",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureSignerCheck",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureTrustAnchor",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureUnverifiableReason",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureValidityCheck",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignatureValidityFailure",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerAddress",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerDigestAlgorithm",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerFingerprint",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerIssuer",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerKeyId",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerName",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerPublicKeyAlgorithm",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerSerialNumber",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerValidFrom",
                table: "stored_emails");

            migrationBuilder.DropColumn(
                name: "SignerValidUntil",
                table: "stored_emails");
        }
    }
}
