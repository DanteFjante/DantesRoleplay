using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "snapshot_package",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ScopeContractId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ScopeContractVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ProducerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProducerVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentEncoding = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BoundaryFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DigestAlgorithm = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ContentDigest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ByteCount = table.Column<long>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RootOperationId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Availability = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_snapshot_package", x => x.Id);
                    table.CheckConstraint("CK_snapshot_package_availability", "\"Availability\" = 'available'");
                    table.CheckConstraint("CK_snapshot_package_boundary_fingerprint", "length(\"BoundaryFingerprint\") = 64 AND \"BoundaryFingerprint\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_snapshot_package_byte_count", "\"ByteCount\" BETWEEN 1 AND 1048576 AND \"ByteCount\" = length(\"Content\")");
                    table.CheckConstraint("CK_snapshot_package_content_digest", "length(\"ContentDigest\") = 64 AND \"ContentDigest\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_snapshot_package_digest_algorithm", "\"DigestAlgorithm\" = 'sha256'");
                    table.CheckConstraint("CK_snapshot_package_encoding", "\"ContentEncoding\" = 'dantes-canonical-json-v1'");
                    table.CheckConstraint("CK_snapshot_package_id", "length(\"Id\") = 41 AND substr(\"Id\", 1, 9) = 'snapshot.' AND substr(\"Id\", 10) NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_snapshot_package_producer_version", "\"ProducerVersion\" > 0");
                    table.CheckConstraint("CK_snapshot_package_root_operation", "length(\"RootOperationId\") = 32 AND \"RootOperationId\" NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_snapshot_package_scope_version", "\"ScopeContractVersion\" > 0");
                });

            migrationBuilder.Sql("""
                CREATE TRIGGER snapshot_package_no_update
                BEFORE UPDATE ON snapshot_package
                BEGIN
                    SELECT RAISE(ABORT, 'SNAPSHOT_PACKAGE_IMMUTABLE');
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER snapshot_package_no_delete
                BEFORE DELETE ON snapshot_package
                BEGIN
                    SELECT RAISE(ABORT, 'SNAPSHOT_PACKAGE_IMMUTABLE');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS snapshot_package_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS snapshot_package_no_update;");

            migrationBuilder.DropTable(
                name: "snapshot_package");
        }
    }
}
