using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations;

[DbContext(typeof(DantesRoleplayDbContext))]
[Migration("20260822193650_GenericInformation")]
public partial class GenericInformation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "information_source",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                ScopeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                MetadataSchemaJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Revision = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_information_source", x => x.Id);
                table.CheckConstraint("CK_information_source_revision", "\"Revision\" > 0");
                table.CheckConstraint("CK_information_source_metadata_schema", "json_valid(\"MetadataSchemaJson\")");
            });
        migrationBuilder.CreateTable(
            name: "information_action_contract",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                ScopeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                ExecutorId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                InputSchemaJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                RuleRecordIdsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Revision = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_information_action_contract", x => x.Id);
                table.CheckConstraint("CK_information_action_contract_revision", "\"Revision\" > 0");
                table.CheckConstraint("CK_information_action_contract_input_schema", "json_valid(\"InputSchemaJson\")");
                table.CheckConstraint("CK_information_action_contract_rule_records", "json_valid(\"RuleRecordIdsJson\")");
            });
        migrationBuilder.CreateTable(
            name: "information_record",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                SourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                MetadataJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Revision = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_information_record", x => x.Id);
                table.ForeignKey("FK_information_record_information_source_SourceId", x => x.SourceId, "information_source", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_information_record_revision", "\"Revision\" > 0");
                table.CheckConstraint("CK_information_record_metadata", "json_valid(\"MetadataJson\")");
            });
        migrationBuilder.CreateIndex(name: "IX_information_source_ScopeId_Id", table: "information_source", columns: new[] { "ScopeId", "Id" });
        migrationBuilder.CreateIndex(name: "IX_information_action_contract_ScopeId_Id", table: "information_action_contract", columns: new[] { "ScopeId", "Id" });
        migrationBuilder.CreateIndex(name: "IX_information_record_SourceId_Id", table: "information_record", columns: new[] { "SourceId", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "information_record");
        migrationBuilder.DropTable(name: "information_action_contract");
        migrationBuilder.DropTable(name: "information_source");
    }
}
