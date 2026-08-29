using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <summary>
    /// Forward migration for the audit and contract fields added after Initial shipped.
    ///
    /// Written as a delta rather than by regenerating Initial. Replacing an applied migration
    /// looks tidier and is wrong the moment a database exists: EF replays the new Initial against
    /// a schema that already has those tables and fails with "table already exists". A database
    /// created from Initial must be able to reach the current schema by moving forward.
    ///
    /// Columns are NOT NULL to match the model, so each carries an empty-string default for the
    /// rows already present.
    ///
    /// Note SourceHash is absent here: it shipped in Initial. The columns a delta needs come from
    /// reading the previous migration, not from recalling which changes felt recent.
    /// </summary>
    public partial class AuditFieldsAndGoverns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The audit log now separates what the agent CLAIMED it consulted from what it was
            // observed to read, so the single free-text column is replaced by two.
            // Native DROP COLUMN rather than migrationBuilder.DropColumn. EF implements DropColumn
            // on SQLite by rebuilding the table, which needs "PRAGMA foreign_keys = 0" — and that
            // pragma cannot run inside the migration transaction, so EF warns. The operation table
            // has no foreign keys either way, but the warning is worth removing rather than
            // explaining: the next column dropped from a table that DOES have them would look
            // identical and would not be safe. A test now asserts no migration needs this.
            migrationBuilder.Sql("ALTER TABLE \"operation\" DROP COLUMN \"ProceduresUsed\";");

            migrationBuilder.AddColumn<string>(
                name: "ProceduresCited", table: "operation",
                type: "TEXT", nullable: false, defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProceduresRead", table: "operation",
                type: "TEXT", nullable: false, defaultValue: "");

            // The primary id an operation acted on. Also what the observed-reads derivation keys on.
            migrationBuilder.AddColumn<string>(
                name: "Subject", table: "operation",
                type: "TEXT", maxLength: 200, nullable: false, defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Governs", table: "procedure_contract_version",
                type: "TEXT", maxLength: 500, nullable: false, defaultValue: "");

            // Supports the history filters and the observed-reads query, which runs on every write.
            migrationBuilder.CreateIndex(
                name: "IX_operation_Subject", table: "operation", column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_operation_Tool_Timestamp", table: "operation",
                columns: ["Tool", "Timestamp"]);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_operation_Tool_Timestamp", table: "operation");
            migrationBuilder.DropIndex(name: "IX_operation_Subject", table: "operation");

            migrationBuilder.DropColumn(name: "Governs", table: "procedure_contract_version");
            migrationBuilder.DropColumn(name: "Subject", table: "operation");
            migrationBuilder.DropColumn(name: "ProceduresRead", table: "operation");
            migrationBuilder.DropColumn(name: "ProceduresCited", table: "operation");

            migrationBuilder.AddColumn<string>(
                name: "ProceduresUsed", table: "operation",
                type: "TEXT", nullable: false, defaultValue: "");
        }
    }
}
