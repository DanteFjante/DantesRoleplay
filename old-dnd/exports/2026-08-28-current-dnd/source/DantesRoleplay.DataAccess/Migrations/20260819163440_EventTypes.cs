using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class EventTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_type",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_type", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "event_type_version",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventTypeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadSchema = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeNote = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_type_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_type_version_event_type_EventTypeId",
                        column: x => x.EventTypeId,
                        principalTable: "event_type",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_type_Category",
                table: "event_type",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_event_type_Scope",
                table: "event_type",
                column: "Scope");

            migrationBuilder.CreateIndex(
                name: "IX_event_type_Status",
                table: "event_type",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_event_type_version_EventTypeId_Version",
                table: "event_type_version",
                columns: new[] { "EventTypeId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_type_version");

            migrationBuilder.DropTable(
                name: "event_type");
        }
    }
}
