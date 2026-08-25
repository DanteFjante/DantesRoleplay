using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class HostSettingOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "host_setting_override",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    AppliedVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_setting_override", x => x.Key);
                    table.CheckConstraint("CK_host_setting_override_versions", "\"CurrentVersion\" > 0 AND \"AppliedVersion\" >= 0 AND \"AppliedVersion\" <= \"CurrentVersion\"");
                });

            migrationBuilder.CreateTable(
                name: "host_setting_override_version",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SettingKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ValueJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_setting_override_version", x => x.Id);
                    table.CheckConstraint("CK_host_setting_override_version_number", "\"Version\" > 0");
                    table.CheckConstraint("CK_host_setting_override_version_operation", "length(\"OperationId\") = 32 AND \"OperationId\" NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_host_setting_override_version_host_setting_override_SettingKey",
                        column: x => x.SettingKey,
                        principalTable: "host_setting_override",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_host_setting_override_version_operation_OperationId",
                        column: x => x.OperationId,
                        principalTable: "operation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_host_setting_override_version_OperationId",
                table: "host_setting_override_version",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_host_setting_override_version_SettingKey_Version",
                table: "host_setting_override_version",
                columns: new[] { "SettingKey", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_setting_override_version");

            migrationBuilder.DropTable(
                name: "host_setting_override");
        }
    }
}
