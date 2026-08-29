using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TriggerSchedulingNotificationSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite rebuilds the definition table when adding its new checks. Custom triggers
            // are not model metadata and therefore must be restored after that rebuild completes.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_one_time_definition_immutable_update;
                DROP TRIGGER IF EXISTS trigger_one_time_definition_immutable_delete;
                CREATE TRIGGER trigger_one_time_definition_immutable_update
                BEFORE UPDATE ON trigger_one_time_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;
                CREATE TRIGGER trigger_one_time_definition_immutable_delete
                BEFORE DELETE ON trigger_one_time_definition
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_SCHEDULING_IMMUTABLE'); END;

                DROP TRIGGER IF EXISTS trigger_one_time_definition_slice5_insert_guard;
                CREATE TRIGGER trigger_one_time_definition_slice5_insert_guard
                BEFORE INSERT ON trigger_one_time_definition
                WHEN NEW.Lifecycle NOT IN ('active', 'cancelled')
                  OR length(NEW.NotificationTopic) NOT BETWEEN 1 AND 200
                  OR length(NEW.NotificationSubject) NOT BETWEEN 1 AND 400
                  OR length(CAST(NEW.NotificationBody AS BLOB)) > 16384
                  OR (NEW.NotificationStateSpaceId IS NOT NULL
                      AND length(NEW.NotificationStateSpaceId) NOT BETWEEN 1 AND 200)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_NOTIFICATION_TARGET_INVALID'); END;

                DROP TRIGGER IF EXISTS trigger_one_time_notification_entity_insert_guard;
                CREATE TRIGGER trigger_one_time_notification_entity_insert_guard
                BEFORE INSERT ON trigger_one_time_notification_entity
                WHEN NOT EXISTS (
                    SELECT 1 FROM trigger_one_time_definition definition
                    WHERE definition.ApplicationId = NEW.ApplicationId
                      AND definition.Id = NEW.TriggerId
                      AND definition.Version = NEW.TriggerVersion
                      AND definition.NotificationStateSpaceId = NEW.StateSpaceId)
                BEGIN SELECT RAISE(ABORT, 'TRIGGER_NOTIFICATION_LINK_INVALID'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trigger_one_time_notification_entity_insert_guard;
                DROP TRIGGER IF EXISTS trigger_one_time_definition_slice5_insert_guard;
                """);
        }
    }
}
