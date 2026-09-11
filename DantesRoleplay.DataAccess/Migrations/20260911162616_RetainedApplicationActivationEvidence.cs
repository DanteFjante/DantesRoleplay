using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RetainedApplicationActivationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreparationVersion",
                table: "system_application_activation_revision",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RetainedBytes",
                table: "system_application_activation_document_evidence",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // These independent nullable columns need no table rebuild or foreign-key suspension.
            // A downgrade discards retained bytes; operational rollback must restore its backup.
            migrationBuilder.Sql("ALTER TABLE system_application_activation_revision DROP COLUMN PreparationVersion;");
            migrationBuilder.Sql("ALTER TABLE system_application_activation_document_evidence DROP COLUMN RetainedBytes;");
        }
    }
}
