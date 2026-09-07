using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.DataAccess.Migrations;

// Infrastructure-only table: deliberately not an EF entity. Trigger coverage is installed after
// schema initialization, since application and web tables use separate migration histories.
[DbContext(typeof(DantesRoleplayDbContext))]
[Migration("20260907180000_UntrackedChangeRecovery")]
public sealed class UntrackedChangeRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE system_change_recovery (
            Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
            StateVersion INTEGER NOT NULL CHECK (typeof(StateVersion) = 'integer' AND StateVersion >= 0),
            OtherVersion INTEGER NOT NULL CHECK (typeof(OtherVersion) = 'integer' AND OtherVersion >= 0),
            SchemaVersion INTEGER NOT NULL);
        INSERT INTO system_change_recovery VALUES (1, 0, 0, -1);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new InvalidOperationException("Restore a pre-migration backup to downgrade change recovery; do not leave installed triggers without their marker table.");
}
