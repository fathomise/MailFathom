// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentConversationFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_conversation_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ByteLength = table.Column<long>(type: "bigint", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TextOutcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PageCount = table.Column<int>(type: "integer", nullable: false),
                    PageKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Segments = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_conversation_files", x => x.Id);
                    table.ForeignKey(
                        name: "fk_agent_conversation_files_agent_conversations",
                        column: x => x.ConversationId,
                        principalTable: "agent_conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_agent_conversation_files_stored_files",
                        column: x => x.Id,
                        principalTable: "stored_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_conversation_file_embeddings",
                columns: table => new
                {
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_conversation_file_embeddings", x => new { x.FileId, x.Ordinal, x.EmbeddingProfileId });
                    table.CheckConstraint("ck_agent_conversation_file_embeddings_dimension", "vector_dims(\"Embedding\") = \"Dimension\"");
                    table.ForeignKey(
                        name: "fk_agent_conversation_file_embeddings_embedding_profiles",
                        columns: x => new { x.EmbeddingProfileId, x.Dimension },
                        principalTable: "embedding_profiles",
                        principalColumns: new[] { "Id", "Dimension" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_conversation_file_embeddings_files",
                        column: x => x.FileId,
                        principalTable: "agent_conversation_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_conversation_file_embeddings_profile",
                table: "agent_conversation_file_embeddings",
                columns: new[] { "EmbeddingProfileId", "Dimension" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_conversation_files_conversation",
                table: "agent_conversation_files",
                columns: new[] { "ConversationId", "AddedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_conversation_file_embeddings");

            migrationBuilder.DropTable(
                name: "agent_conversation_files");
        }
    }
}
