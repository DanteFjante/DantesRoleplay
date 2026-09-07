using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.DataAccess.Migrations;

[DbContext(typeof(DantesRoleplayDbContext))]
[Migration("20260907210000_ProjectionRegistryGeneration")]
public sealed class ProjectionRegistryGeneration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "system_projection_registry_generation",
            columns: table => new
            {
                ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                Generation = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_system_projection_registry_generation", x => x.ApplicationId);
                table.CheckConstraint("CK_system_projection_registry_generation_value", "\"Generation\" >= 0");
                table.ForeignKey(
                    name: "FK_system_projection_registry_generation_system_application_ApplicationId",
                    column: x => x.ApplicationId,
                    principalTable: "system_application",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql("""
            INSERT INTO system_projection_registry_generation (ApplicationId, Generation)
            SELECT d.ApplicationId, COUNT(*)
            FROM system_projection_definition AS d
            INNER JOIN system_projection_definition_version AS v ON v.QualifiedId = d.QualifiedId
            GROUP BY d.ApplicationId;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "system_projection_registry_generation");
}
