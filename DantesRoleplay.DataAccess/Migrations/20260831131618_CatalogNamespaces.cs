using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CatalogNamespaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_catalog_namespace",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    AllowedKindsJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AliasesJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DisabledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_catalog_namespace", x => x.Id);
                    table.CheckConstraint("CK_system_catalog_namespace_aliases", "json_valid(\"AliasesJson\") AND json_type(\"AliasesJson\") = 'array'");
                    table.CheckConstraint("CK_system_catalog_namespace_kinds", "json_valid(\"AllowedKindsJson\") AND json_type(\"AllowedKindsJson\") = 'array'");
                    table.ForeignKey(
                        name: "FK_system_catalog_namespace_system_catalog_namespace_ParentId",
                        column: x => x.ParentId,
                        principalTable: "system_catalog_namespace",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_catalog_namespace_overlay",
                columns: table => new
                {
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    HigherNamespaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LowerNamespaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RecordKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_catalog_namespace_overlay", x => new { x.ApplicationId, x.HigherNamespaceId, x.LowerNamespaceId, x.RecordKind });
                    table.ForeignKey(
                        name: "FK_system_catalog_namespace_overlay_system_catalog_namespace_HigherNamespaceId",
                        column: x => x.HigherNamespaceId,
                        principalTable: "system_catalog_namespace",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_system_catalog_namespace_overlay_system_catalog_namespace_LowerNamespaceId",
                        column: x => x.LowerNamespaceId,
                        principalTable: "system_catalog_namespace",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_catalog_namespace_DisabledAtUtc_Id",
                table: "system_catalog_namespace",
                columns: new[] { "DisabledAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_system_catalog_namespace_ParentId",
                table: "system_catalog_namespace",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_system_catalog_namespace_overlay_HigherNamespaceId",
                table: "system_catalog_namespace_overlay",
                column: "HigherNamespaceId");

            migrationBuilder.CreateIndex(
                name: "IX_system_catalog_namespace_overlay_LowerNamespaceId",
                table: "system_catalog_namespace_overlay",
                column: "LowerNamespaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_catalog_namespace_overlay");

            migrationBuilder.DropTable(
                name: "system_catalog_namespace");
        }
    }
}
