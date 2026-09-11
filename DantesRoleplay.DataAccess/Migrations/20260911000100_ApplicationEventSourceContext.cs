using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.DataAccess.Migrations;

[DbContext(typeof(DantesRoleplayDbContext))]
[Migration("20260911000100_ApplicationEventSourceContext")]
public sealed class ApplicationEventSourceContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ApplicationId",
            table: "event",
            type: "TEXT",
            maxLength: 63,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "StateSpaceId",
            table: "event",
            type: "TEXT",
            maxLength: 200,
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_event_ApplicationId_StateSpaceId_TypeId_Timestamp",
            table: "event",
            columns: new[] { "ApplicationId", "StateSpaceId", "TypeId", "Timestamp" });
        migrationBuilder.AddColumn<string>(
            name: "ApplicationId",
            table: "subscription_version",
            type: "TEXT",
            maxLength: 63,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "StateSpaceId",
            table: "subscription_version",
            type: "TEXT",
            maxLength: 200,
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_subscription_version_ApplicationId_StateSpaceId",
            table: "subscription_version",
            columns: new[] { "ApplicationId", "StateSpaceId" });

        // SQLite cannot add a table check after adding columns. The two guards retain the
        // same invariant for migrated databases; the model check covers newly-created stores.
        migrationBuilder.Sql("""
            CREATE TRIGGER event_application_source_pair_insert
            BEFORE INSERT ON event
            WHEN (NEW.ApplicationId IS NULL) <> (NEW.StateSpaceId IS NULL)
            BEGIN
                SELECT RAISE(ABORT, 'event application source must be a complete pair');
            END;
            CREATE TRIGGER event_application_source_pair_update
            BEFORE UPDATE OF ApplicationId, StateSpaceId ON event
            WHEN (NEW.ApplicationId IS NULL) <> (NEW.StateSpaceId IS NULL)
            BEGIN
                SELECT RAISE(ABORT, 'event application source must be a complete pair');
            END;
            CREATE TRIGGER subscription_version_application_source_pair_insert
            BEFORE INSERT ON subscription_version
            WHEN (NEW.ApplicationId IS NULL) <> (NEW.StateSpaceId IS NULL)
            BEGIN
                SELECT RAISE(ABORT, 'subscription application source must be a complete pair');
            END;
            CREATE TRIGGER subscription_version_application_source_pair_update
            BEFORE UPDATE OF ApplicationId, StateSpaceId ON subscription_version
            WHEN (NEW.ApplicationId IS NULL) <> (NEW.StateSpaceId IS NULL)
            BEGIN
                SELECT RAISE(ABORT, 'subscription application source must be a complete pair');
            END;
            """);

        migrationBuilder.CreateTable(
            name: "event_component_snapshot",
            columns: table => new
            {
                EventId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                EntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                QualifiedTypeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                TypeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                BeforeRevision = table.Column<int>(type: "INTEGER", nullable: true),
                AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                AfterRevision = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_event_component_snapshot", x => x.EventId);
                table.ForeignKey(
                    name: "FK_event_component_snapshot_event_EventId",
                    column: x => x.EventId,
                    principalTable: "event",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(
            name: "IX_event_component_snapshot_EntityId_QualifiedTypeId",
            table: "event_component_snapshot",
            columns: new[] { "EntityId", "QualifiedTypeId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "event_component_snapshot");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS subscription_version_application_source_pair_update;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS subscription_version_application_source_pair_insert;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS event_application_source_pair_update;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS event_application_source_pair_insert;");
        migrationBuilder.DropIndex(name: "IX_subscription_version_ApplicationId_StateSpaceId", table: "subscription_version");
        migrationBuilder.Sql("ALTER TABLE subscription_version DROP COLUMN ApplicationId;");
        migrationBuilder.Sql("ALTER TABLE subscription_version DROP COLUMN StateSpaceId;");
        migrationBuilder.DropIndex(name: "IX_event_ApplicationId_StateSpaceId_TypeId_Timestamp", table: "event");
        migrationBuilder.Sql("ALTER TABLE event DROP COLUMN ApplicationId;");
        migrationBuilder.Sql("ALTER TABLE event DROP COLUMN StateSpaceId;");
    }
}
