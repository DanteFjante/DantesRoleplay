using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <summary>
    /// Adds mechanic storage: identity plus append-only versions, shaped exactly like the
    /// procedure tables.
    ///
    /// Two new tables and nothing else. The entity-component design means the world schema does
    /// not move when the game grows, but the KERNEL still gains tables when a subsystem lands —
    /// that distinction is why this project migrates rather than using EnsureCreated.
    ///
    /// Note there is no column here describing what a mechanic does. The source is text and the
    /// requirements are JSON; the database is deliberately incapable of understanding either.
    /// </summary>
    public partial class Mechanics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mechanic",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mechanic", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mechanic_version",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MechanicId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Matches = table.Column<string>(type: "TEXT", nullable: false),
                    Requirements = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeNote = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mechanic_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mechanic_version_mechanic_MechanicId",
                        column: x => x.MechanicId,
                        principalTable: "mechanic",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mechanic_Category",
                table: "mechanic",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_mechanic_Scope",
                table: "mechanic",
                column: "Scope");

            migrationBuilder.CreateIndex(
                name: "IX_mechanic_Status",
                table: "mechanic",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_mechanic_version_MechanicId_Version",
                table: "mechanic_version",
                columns: new[] { "MechanicId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mechanic_version");

            migrationBuilder.DropTable(
                name: "mechanic");
        }
    }
}
