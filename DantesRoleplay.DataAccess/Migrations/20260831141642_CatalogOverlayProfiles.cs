using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations;

[DbContext(typeof(DantesRoleplayDbContext))]
[Migration("20260831141642_CatalogOverlayProfiles")]
public partial class CatalogOverlayProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "system_catalog_namespace_overlay_profile",
            columns: table => new
            {
                ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                ProfileId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_system_catalog_namespace_overlay_profile",
                    value => new { value.ApplicationId, value.ProfileId });
            });

        migrationBuilder.Sql(
            """
            INSERT INTO system_catalog_namespace_overlay_profile
                (ApplicationId, ProfileId, Description, CreatedAtUtc)
            SELECT DISTINCT ApplicationId, 'legacy-default',
                'Migrated implicit namespace overlay profile.', CURRENT_TIMESTAMP
            FROM system_catalog_namespace_overlay;
            """);

        migrationBuilder.Sql(
            """
            CREATE TABLE __temp_system_catalog_namespace_overlay (
                ApplicationId TEXT NOT NULL,
                ProfileId TEXT NOT NULL,
                HigherNamespaceId TEXT NOT NULL,
                LowerNamespaceId TEXT NOT NULL,
                RecordKind TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                CONSTRAINT PK_system_catalog_namespace_overlay PRIMARY KEY
                    (ApplicationId, ProfileId, HigherNamespaceId, LowerNamespaceId, RecordKind),
                CONSTRAINT FK_system_catalog_namespace_overlay_system_catalog_namespace_overlay_profile_ApplicationId_ProfileId
                    FOREIGN KEY (ApplicationId, ProfileId)
                    REFERENCES system_catalog_namespace_overlay_profile (ApplicationId, ProfileId)
                    ON DELETE RESTRICT,
                CONSTRAINT FK_system_catalog_namespace_overlay_system_catalog_namespace_HigherNamespaceId
                    FOREIGN KEY (HigherNamespaceId) REFERENCES system_catalog_namespace (Id)
                    ON DELETE RESTRICT,
                CONSTRAINT FK_system_catalog_namespace_overlay_system_catalog_namespace_LowerNamespaceId
                    FOREIGN KEY (LowerNamespaceId) REFERENCES system_catalog_namespace (Id)
                    ON DELETE RESTRICT
            );
            INSERT INTO __temp_system_catalog_namespace_overlay
                (ApplicationId, ProfileId, HigherNamespaceId, LowerNamespaceId, RecordKind, CreatedAtUtc)
            SELECT ApplicationId, 'legacy-default', HigherNamespaceId, LowerNamespaceId, RecordKind, CreatedAtUtc
            FROM system_catalog_namespace_overlay;
            DROP TABLE system_catalog_namespace_overlay;
            ALTER TABLE __temp_system_catalog_namespace_overlay RENAME TO system_catalog_namespace_overlay;
            CREATE INDEX IX_system_catalog_namespace_overlay_HigherNamespaceId
                ON system_catalog_namespace_overlay (HigherNamespaceId);
            CREATE INDEX IX_system_catalog_namespace_overlay_LowerNamespaceId
                ON system_catalog_namespace_overlay (LowerNamespaceId);
            """);

        migrationBuilder.CreateTable(
            name: "system_catalog_namespace_resolution_key",
            columns: table => new
            {
                ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                ProfileId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                ResolutionKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                RecordKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_system_catalog_namespace_resolution_key",
                    value => new { value.ApplicationId, value.ProfileId, value.ResolutionKey });
                table.ForeignKey(
                    name: "FK_system_catalog_namespace_resolution_key_system_catalog_namespace_overlay_profile_ApplicationId_ProfileId",
                    columns: value => new { value.ApplicationId, value.ProfileId },
                    principalTable: "system_catalog_namespace_overlay_profile",
                    principalColumns: new[] { "ApplicationId", "ProfileId" },
                    onDelete: ReferentialAction.Restrict);
            });

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "system_catalog_namespace_resolution_key");

        migrationBuilder.Sql(
            """
            CREATE TABLE __temp_system_catalog_namespace_overlay (
                ApplicationId TEXT NOT NULL,
                HigherNamespaceId TEXT NOT NULL,
                LowerNamespaceId TEXT NOT NULL,
                RecordKind TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                CONSTRAINT PK_system_catalog_namespace_overlay PRIMARY KEY
                    (ApplicationId, HigherNamespaceId, LowerNamespaceId, RecordKind),
                CONSTRAINT FK_system_catalog_namespace_overlay_system_catalog_namespace_HigherNamespaceId
                    FOREIGN KEY (HigherNamespaceId) REFERENCES system_catalog_namespace (Id)
                    ON DELETE RESTRICT,
                CONSTRAINT FK_system_catalog_namespace_overlay_system_catalog_namespace_LowerNamespaceId
                    FOREIGN KEY (LowerNamespaceId) REFERENCES system_catalog_namespace (Id)
                    ON DELETE RESTRICT
            );
            INSERT INTO __temp_system_catalog_namespace_overlay
                (ApplicationId, HigherNamespaceId, LowerNamespaceId, RecordKind, CreatedAtUtc)
            SELECT ApplicationId, HigherNamespaceId, LowerNamespaceId, RecordKind, CreatedAtUtc
            FROM system_catalog_namespace_overlay;
            DROP TABLE system_catalog_namespace_overlay;
            ALTER TABLE __temp_system_catalog_namespace_overlay RENAME TO system_catalog_namespace_overlay;
            CREATE INDEX IX_system_catalog_namespace_overlay_HigherNamespaceId
                ON system_catalog_namespace_overlay (HigherNamespaceId);
            CREATE INDEX IX_system_catalog_namespace_overlay_LowerNamespaceId
                ON system_catalog_namespace_overlay (LowerNamespaceId);
            """);

        migrationBuilder.DropTable(name: "system_catalog_namespace_overlay_profile");
    }
}
