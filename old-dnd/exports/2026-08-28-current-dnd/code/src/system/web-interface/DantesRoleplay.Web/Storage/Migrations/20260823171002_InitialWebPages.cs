using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.Web.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWebPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "web_page",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ActiveRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_page", x => x.Id);
                    table.CheckConstraint("CK_web_page_active_revision", "\"ActiveRevision\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "web_page_revision",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PageId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Html = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_page_revision", x => x.Id);
                    table.CheckConstraint("CK_web_page_revision_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_web_page_revision_web_page_PageId",
                        column: x => x.PageId,
                        principalTable: "web_page",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_web_page_revision_PageId_Revision",
                table: "web_page_revision",
                columns: new[] { "PageId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "web_page_revision");

            migrationBuilder.DropTable(
                name: "web_page");
        }
    }
}
