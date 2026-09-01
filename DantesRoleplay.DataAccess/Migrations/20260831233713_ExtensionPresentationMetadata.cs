using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ExtensionPresentationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Classification",
                table: "system_application_extension",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "third-party");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "system_application_extension",
                type: "TEXT",
                maxLength: 120,
                nullable: false,
                defaultValue: "extension");

            migrationBuilder.AddColumn<int>(
                name: "RegistrationSchemaVersion",
                table: "system_application_extension",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("""
                UPDATE system_application_extension
                SET DisplayName = ExtensionId,
                    Classification = 'third-party',
                    RegistrationSchemaVersion = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Classification",
                table: "system_application_extension");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "system_application_extension");

            migrationBuilder.DropColumn(
                name: "RegistrationSchemaVersion",
                table: "system_application_extension");
        }
    }
}
