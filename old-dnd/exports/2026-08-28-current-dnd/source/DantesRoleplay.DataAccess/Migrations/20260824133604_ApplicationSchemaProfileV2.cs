using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationSchemaProfileV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's generated SQLite check-constraint change disables foreign keys outside the
            // transaction. Deferred checking lets the referenced parent table be rebuilt while
            // preserving ECS/projection children and migration atomicity.
            migrationBuilder.Sql("PRAGMA defer_foreign_keys = ON;");
            Rebuild(migrationBuilder,
                "\"ProfileId\" IN ('system-json-schema-2020-12/v1', 'system-json-schema-2020-12/v2')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The v1 check makes this fail transactionally instead of deleting v2 history.
            migrationBuilder.Sql("PRAGMA defer_foreign_keys = ON;");
            Rebuild(migrationBuilder, "\"ProfileId\" = 'system-json-schema-2020-12/v1'");
        }

        private static void Rebuild(MigrationBuilder migrationBuilder, string profileConstraint)
        {
            migrationBuilder.Sql($$"""
                CREATE TABLE "system_component_type_version_rebuilt" (
                    "QualifiedId" TEXT NOT NULL,
                    "Version" INTEGER NOT NULL,
                    "ProfileId" TEXT NOT NULL,
                    "SchemaJson" TEXT NOT NULL,
                    "SchemaHash" TEXT NOT NULL,
                    "CreatedAtUtc" TEXT NOT NULL,
                    CONSTRAINT "PK_system_component_type_version" PRIMARY KEY ("QualifiedId", "Version"),
                    CONSTRAINT "CK_system_component_type_version_number" CHECK ("Version" > 0),
                    CONSTRAINT "CK_system_component_type_version_hash" CHECK (length("SchemaHash") = 64 AND "SchemaHash" NOT GLOB '*[^0-9A-F]*'),
                    CONSTRAINT "CK_system_component_type_version_profile" CHECK ({{profileConstraint}}),
                    CONSTRAINT "CK_system_component_type_version_schema_json" CHECK (json_valid("SchemaJson")),
                    CONSTRAINT "FK_system_component_type_version_system_component_type_QualifiedId"
                        FOREIGN KEY ("QualifiedId") REFERENCES "system_component_type" ("QualifiedId") ON DELETE RESTRICT
                );
                INSERT INTO "system_component_type_version_rebuilt"
                    ("QualifiedId", "Version", "ProfileId", "SchemaJson", "SchemaHash", "CreatedAtUtc")
                SELECT "QualifiedId", "Version", "ProfileId", "SchemaJson", "SchemaHash", "CreatedAtUtc"
                FROM "system_component_type_version";
                DROP TABLE "system_component_type_version";
                ALTER TABLE "system_component_type_version_rebuilt" RENAME TO "system_component_type_version";
                """);
        }
    }
}
