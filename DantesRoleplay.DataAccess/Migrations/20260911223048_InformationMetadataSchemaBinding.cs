using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InformationMetadataSchemaBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetadataSchemaHash",
                table: "information_source",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataSchemaQualifiedId",
                table: "information_source",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetadataSchemaVersion",
                table: "information_source",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetadataSchemaSourceRevision",
                table: "information_record",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE information_record
                SET MetadataSchemaSourceRevision = (
                    SELECT Revision FROM information_source
                    WHERE information_source.Id = information_record.SourceId
                );
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER information_source_metadata_schema_reference_insert
                BEFORE INSERT ON information_source
                WHEN (NEW.MetadataSchemaQualifiedId IS NULL
                        AND (NEW.MetadataSchemaVersion IS NOT NULL OR NEW.MetadataSchemaHash IS NOT NULL))
                    OR (NEW.MetadataSchemaQualifiedId IS NOT NULL
                        AND (NEW.MetadataSchemaVersion IS NULL OR NEW.MetadataSchemaVersion <= 0
                            OR NEW.MetadataSchemaHash IS NULL OR length(NEW.MetadataSchemaHash) != 64
                            OR NEW.MetadataSchemaHash GLOB '*[^0-9A-F]*'))
                BEGIN SELECT RAISE(ABORT, 'INFORMATION_SOURCE_SCHEMA_REFERENCE_INVALID'); END;
                CREATE TRIGGER information_source_metadata_schema_reference_update
                BEFORE UPDATE OF MetadataSchemaQualifiedId, MetadataSchemaVersion, MetadataSchemaHash ON information_source
                WHEN (NEW.MetadataSchemaQualifiedId IS NULL
                        AND (NEW.MetadataSchemaVersion IS NOT NULL OR NEW.MetadataSchemaHash IS NOT NULL))
                    OR (NEW.MetadataSchemaQualifiedId IS NOT NULL
                        AND (NEW.MetadataSchemaVersion IS NULL OR NEW.MetadataSchemaVersion <= 0
                            OR NEW.MetadataSchemaHash IS NULL OR length(NEW.MetadataSchemaHash) != 64
                            OR NEW.MetadataSchemaHash GLOB '*[^0-9A-F]*'))
                BEGIN SELECT RAISE(ABORT, 'INFORMATION_SOURCE_SCHEMA_REFERENCE_INVALID'); END;
                CREATE TRIGGER information_record_metadata_schema_revision_insert
                BEFORE INSERT ON information_record WHEN NEW.MetadataSchemaSourceRevision <= 0
                BEGIN SELECT RAISE(ABORT, 'INFORMATION_RECORD_SCHEMA_REVISION_INVALID'); END;
                CREATE TRIGGER information_record_metadata_schema_revision_update
                BEFORE UPDATE OF MetadataSchemaSourceRevision ON information_record WHEN NEW.MetadataSchemaSourceRevision <= 0
                BEGIN SELECT RAISE(ABORT, 'INFORMATION_RECORD_SCHEMA_REVISION_INVALID'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE __information_metadata_schema_downgrade_guard (
                    blocked INTEGER NOT NULL,
                    CONSTRAINT retained_information_metadata_schema_prevents_downgrade CHECK (blocked = 0)
                );
                INSERT INTO __information_metadata_schema_downgrade_guard (blocked)
                SELECT 1 WHERE EXISTS (SELECT 1 FROM information_record)
                    OR EXISTS (SELECT 1 FROM information_source WHERE MetadataSchemaQualifiedId IS NOT NULL);
                DROP TABLE __information_metadata_schema_downgrade_guard;
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS information_source_metadata_schema_reference_insert;
                DROP TRIGGER IF EXISTS information_source_metadata_schema_reference_update;
                DROP TRIGGER IF EXISTS information_record_metadata_schema_revision_insert;
                DROP TRIGGER IF EXISTS information_record_metadata_schema_revision_update;
                ALTER TABLE information_source DROP COLUMN MetadataSchemaHash;
                ALTER TABLE information_source DROP COLUMN MetadataSchemaQualifiedId;
                ALTER TABLE information_source DROP COLUMN MetadataSchemaVersion;
                ALTER TABLE information_record DROP COLUMN MetadataSchemaSourceRevision;
                """);
        }
    }
}
