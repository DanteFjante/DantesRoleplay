using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class EventExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_execution",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    SubscriptionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SubscriptionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    MechanicId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MechanicVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Seed = table.Column<long>(type: "INTEGER", nullable: false),
                    ProjectionJson = table.Column<string>(type: "TEXT", nullable: false),
                    OutputJson = table.Column<string>(type: "TEXT", nullable: false),
                    EffectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EventCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", nullable: false),
                    LogJson = table.Column<string>(type: "TEXT", nullable: false),
                    ElapsedMilliseconds = table.Column<int>(type: "INTEGER", nullable: false),
                    LimitHit = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_execution", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_execution_event_EventId",
                        column: x => x.EventId,
                        principalTable: "event",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_execution_EventId_Ordinal",
                table: "event_execution",
                columns: new[] { "EventId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_execution_SubscriptionId",
                table: "event_execution",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_execution");
        }
    }
}
