// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCommitments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AnchorEventId",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnchorLabel",
                table: "tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AnchorThreadId",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AnswerStoredEmailId",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AnsweredAt",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartAddress",
                table: "tasks",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartDisplayName",
                table: "tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartNormalizedAddress",
                table: "tasks",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "tasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Owed");

            migrationBuilder.AddColumn<int>(
                name: "EstimatedMinutes",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlannedMinutes",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlannedStartAt",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_awaiting_answer_thread",
                table: "tasks",
                column: "AnchorThreadId",
                filter: "\"Direction\" = 'Awaited' AND NOT \"IsCompleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_AnchorEventId",
                table: "tasks",
                columns: new[] { "UserId", "AnchorEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_AnchorThreadId",
                table: "tasks",
                columns: new[] { "UserId", "AnchorThreadId" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_CounterpartNormalizedAddress",
                table: "tasks",
                columns: new[] { "UserId", "CounterpartNormalizedAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_UserId_PlannedStartAt",
                table: "tasks",
                columns: new[] { "UserId", "PlannedStartAt" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_anchor_labelled",
                table: "tasks",
                sql: "(\"AnchorLabel\" IS NULL) = (\"AnchorThreadId\" IS NULL AND \"AnchorEventId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_answer_complete",
                table: "tasks",
                sql: "(\"AnswerStoredEmailId\" IS NULL) = (\"AnsweredAt\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_counterpart_complete",
                table: "tasks",
                sql: "(\"CounterpartAddress\" IS NULL) = (\"CounterpartNormalizedAddress\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_plan_complete",
                table: "tasks",
                sql: "(\"PlannedStartAt\" IS NULL) = (\"PlannedMinutes\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_single_anchor",
                table: "tasks",
                sql: "\"AnchorThreadId\" IS NULL OR \"AnchorEventId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tasks_awaiting_answer_thread",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_AnchorEventId",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_AnchorThreadId",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_CounterpartNormalizedAddress",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_UserId_PlannedStartAt",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_anchor_labelled",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_answer_complete",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_counterpart_complete",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_plan_complete",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_single_anchor",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AnchorEventId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AnchorLabel",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AnchorThreadId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AnswerStoredEmailId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AnsweredAt",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "CounterpartAddress",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "CounterpartDisplayName",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "CounterpartNormalizedAddress",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "EstimatedMinutes",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "PlannedMinutes",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "PlannedStartAt",
                table: "tasks");
        }
    }
}
