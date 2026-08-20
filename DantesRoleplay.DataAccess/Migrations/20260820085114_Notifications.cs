using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ExecutionId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RootOperationId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_entity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NotificationId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_entity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_entity_notification_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_CorrelationId_Ordinal",
                table: "notification",
                columns: new[] { "CorrelationId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_State_CreatedAt",
                table: "notification",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_Topic_CreatedAt",
                table: "notification",
                columns: new[] { "Topic", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_entity_EntityId_Id",
                table: "notification_entity",
                columns: new[] { "EntityId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_entity_NotificationId_Ordinal",
                table: "notification_entity",
                columns: new[] { "NotificationId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_entity");

            migrationBuilder.DropTable(
                name: "notification");
        }
    }
}
