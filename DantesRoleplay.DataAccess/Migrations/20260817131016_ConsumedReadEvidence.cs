using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <summary>
    /// Records whether an operation was one that consumes read evidence.
    ///
    /// A separate migration rather than an edit to AuditFieldsAndGoverns, because that one has
    /// already been applied to a real database. Adding a column to an applied migration is the
    /// same mistake as regenerating the initial one, just quieter.
    /// </summary>
    public partial class ConsumedReadEvidence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddColumn<bool>(
                name: "ConsumedReadEvidence", table: "operation",
                type: "INTEGER", nullable: false, defaultValue: false);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            // Native DROP COLUMN, so SQLite does not rebuild the table and need a pragma that
            // cannot run inside the migration transaction.
            migrationBuilder.Sql("ALTER TABLE \"operation\" DROP COLUMN \"ConsumedReadEvidence\";");
    }
}
