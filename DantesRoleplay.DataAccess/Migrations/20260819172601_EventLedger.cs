using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class EventLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TypeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TypeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CausationId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    RootOperationId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "event_entity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_entity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_entity_event_EventId",
                        column: x => x.EventId,
                        principalTable: "event",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_CorrelationId_Sequence",
                table: "event",
                columns: new[] { "CorrelationId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_event_RootOperationId",
                table: "event",
                column: "RootOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_event_TypeId_Timestamp",
                table: "event",
                columns: new[] { "TypeId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_event_entity_EntityId_Id",
                table: "event_entity",
                columns: new[] { "EntityId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_event_entity_EventId_Ordinal",
                table: "event_entity",
                columns: new[] { "EventId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_entity");

            migrationBuilder.DropTable(
                name: "event");
        }
    }
}
