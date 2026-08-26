using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingObservationImmutability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite implements the composite permission FK from the preceding migration by
            // rebuilding trigger_observation. Install these two triggers afterward so that
            // provider-managed rebuild cannot discard them.
            migrationBuilder.Sql("""
                CREATE TRIGGER trigger_observation_immutable_update
                BEFORE UPDATE ON trigger_observation
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_observation_immutable_delete
                BEFORE DELETE ON trigger_observation
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_observation_immutable_update;
                DROP TRIGGER IF EXISTS trigger_observation_immutable_delete;
                """);
        }
    }
}
