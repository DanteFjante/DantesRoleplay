using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.Web.Storage.Migrations
{
    /// <inheritdoc />
    public partial class WebPageResourceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "web_page_resource_identity",
                columns: table => new
                {
                    ContentPageId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    QualifiedTargetId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OwnerApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    SourceOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_page_resource_identity", x => x.ContentPageId);
                    table.ForeignKey(
                        name: "FK_web_page_resource_identity_web_page_ContentPageId",
                        column: x => x.ContentPageId,
                        principalTable: "web_page",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_web_page_resource_identity_QualifiedTargetId",
                table: "web_page_resource_identity",
                column: "QualifiedTargetId",
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER web_page_resource_identity_immutable_update
                BEFORE UPDATE ON web_page_resource_identity
                BEGIN SELECT RAISE(ABORT, 'WEB_PAGE_RESOURCE_IDENTITY_IMMUTABLE'); END;
                CREATE TRIGGER web_page_resource_identity_immutable_delete
                BEFORE DELETE ON web_page_resource_identity
                BEGIN SELECT RAISE(ABORT, 'WEB_PAGE_RESOURCE_IDENTITY_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __web_page_resource_identity_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_web_page_resource_identity_prevents_downgrade CHECK (blocked = 0)
                );
                INSERT INTO __web_page_resource_identity_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (SELECT 1 FROM web_page_resource_identity);
                DROP TABLE __web_page_resource_identity_downgrade_guard;
                DROP TRIGGER IF EXISTS web_page_resource_identity_immutable_delete;
                DROP TRIGGER IF EXISTS web_page_resource_identity_immutable_update;
                """);

            migrationBuilder.DropTable(
                name: "web_page_resource_identity");
        }
    }
}
