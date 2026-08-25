using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.Web.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebPageAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "web_page_asset",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PageRevisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 127, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_page_asset", x => x.Id);
                    table.ForeignKey(
                        name: "FK_web_page_asset_web_page_revision_PageRevisionId",
                        column: x => x.PageRevisionId,
                        principalTable: "web_page_revision",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_web_page_asset_PageRevisionId_Path",
                table: "web_page_asset",
                columns: new[] { "PageRevisionId", "Path" },
                unique: true);

            migrationBuilder.Sql("PRAGMA optimize;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "web_page_asset");
        }
    }
}
