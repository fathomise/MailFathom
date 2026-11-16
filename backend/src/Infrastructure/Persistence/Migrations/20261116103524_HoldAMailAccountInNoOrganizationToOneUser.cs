// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Holds a mail account in no organization to one assigned user in PostgreSQL itself. An index predicate cannot read
    /// the account's row, so each assignment carries whether its account stands in no organization, and the unique index
    /// covers the assignments where it does. Two triggers keep that flag rather than any writer: one computes it on every
    /// insert and update of an assignment, whatever value the statement carried, and one rewrites it on every assignment
    /// of an account whose organization changes, in the statement that changes it. So an older build writing during a
    /// rolling upgrade, which knows nothing of the column, is held to the index exactly as this one is.
    /// </para>
    /// <para>
    /// The first reads the account under a share lock, because the key-share lock an assignment's foreign key takes does
    /// not wait for a change of the account's organization: without it, an assignment and a move landing at once could
    /// each read the other's row as it stood, and leave a second user flagged as outside the index. With it, either the
    /// assignment waits for the move and reads where the account went, or the move waits for the assignment and its
    /// rewrite reaches the row the assignment committed.
    /// </para>
    /// <para>
    /// An account in no organization may already hold several assignments, from before the rule that made such an
    /// account one person's. Those are ended here, before the index is created, keeping the earliest assignment of each
    /// account: every later user is unassigned the way an unassignment does it, so their drafts and recurring sends of
    /// that account go with the assignment, their footer for it goes by cascade, the mailbox and its mail stay with the
    /// user who keeps it, and the record version of each user unassigned moves so every replica composes them again.
    /// That is a break against the database schema, and the operator's way round it is to move such an account and every
    /// user it is assigned to into one organization before upgrading.
    /// </para>
    /// </remarks>
    public partial class HoldAMailAccountInNoOrganizationToOneUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AccountInNoOrganization",
                table: "mail_account_assignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                WITH ended AS (
                    DELETE FROM mail_account_assignments AS assignment
                    USING settings_mail_accounts AS account
                    WHERE account."Id" = assignment."MailAccountId"
                      AND account."OrganizationId" IS NULL
                      AND EXISTS (
                          SELECT 1 FROM mail_account_assignments AS earlier
                          WHERE earlier."MailAccountId" = assignment."MailAccountId"
                            AND (earlier."AssignedAt", earlier."UserId") < (assignment."AssignedAt", assignment."UserId"))
                    RETURNING assignment."UserId", assignment."MailAccountId"
                ),
                drafts AS (
                    DELETE FROM mail_drafts AS draft
                    USING ended
                    WHERE draft."MailboxAccountId" = ended."MailAccountId"::text
                      AND draft."UserId" = ended."UserId"
                ),
                recurring AS (
                    DELETE FROM recurring_sends AS send
                    USING ended
                    WHERE send."MailboxAccountId" = ended."MailAccountId"::text
                      AND send."UserId" = ended."UserId"
                )
                UPDATE settings_accounts AS unassigned
                SET "Version" = "Version" + 1,
                    "EndpointSwitchesVersion" = "EndpointSwitchesVersion" + 1,
                    "UpdatedAt" = now()
                WHERE unassigned."Id" IN (SELECT "UserId" FROM ended);
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION mailfathom_compute_assignment_outside_organization() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    NEW."AccountInNoOrganization" := coalesce(
                        (SELECT account."OrganizationId" IS NULL
                         FROM settings_mail_accounts AS account
                         WHERE account."Id" = NEW."MailAccountId"
                         FOR SHARE),
                        false);

                    RETURN NEW;
                END
                $$;

                CREATE TRIGGER compute_assignment_outside_organization
                    BEFORE INSERT OR UPDATE ON mail_account_assignments
                    FOR EACH ROW EXECUTE FUNCTION mailfathom_compute_assignment_outside_organization();

                CREATE FUNCTION mailfathom_carry_mail_account_organization() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    UPDATE mail_account_assignments
                    SET "AccountInNoOrganization" = NEW."OrganizationId" IS NULL
                    WHERE "MailAccountId" = NEW."Id";

                    RETURN NULL;
                END
                $$;

                CREATE TRIGGER carry_mail_account_organization
                    AFTER UPDATE OF "OrganizationId" ON settings_mail_accounts
                    FOR EACH ROW
                    WHEN (OLD."OrganizationId" IS DISTINCT FROM NEW."OrganizationId")
                    EXECUTE FUNCTION mailfathom_carry_mail_account_organization();

                UPDATE mail_account_assignments AS assignment
                SET "AccountInNoOrganization" = true
                FROM settings_mail_accounts AS account
                WHERE account."Id" = assignment."MailAccountId"
                  AND account."OrganizationId" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_mail_account_assignments_sole_user_outside_organization",
                table: "mail_account_assignments",
                column: "MailAccountId",
                unique: true,
                filter: "\"AccountInNoOrganization\"");
        }

        /// <inheritdoc />
        /// <remarks>The assignments <see cref="Up" /> ended stay ended: nothing records whom they served.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mail_account_assignments_sole_user_outside_organization",
                table: "mail_account_assignments");

            migrationBuilder.Sql(
                """
                DROP TRIGGER carry_mail_account_organization ON settings_mail_accounts;
                DROP FUNCTION mailfathom_carry_mail_account_organization();
                DROP TRIGGER compute_assignment_outside_organization ON mail_account_assignments;
                DROP FUNCTION mailfathom_compute_assignment_outside_organization();
                """);

            migrationBuilder.DropColumn(
                name: "AccountInNoOrganization",
                table: "mail_account_assignments");
        }
    }
}
