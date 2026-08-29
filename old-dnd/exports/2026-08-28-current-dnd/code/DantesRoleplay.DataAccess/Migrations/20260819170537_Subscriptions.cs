using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class Subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_version",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubscriptionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    EventTypeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventMechanicId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    FixedRoleEntityIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    TrackedEntityIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadEqualsJson = table.Column<string>(type: "TEXT", nullable: false),
                    MaxExecutionsPerChain = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeNote = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subscription_version_subscription_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "subscription",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_Category",
                table: "subscription",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_Scope",
                table: "subscription",
                column: "Scope");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_Status",
                table: "subscription",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_version_EventMechanicId",
                table: "subscription_version",
                column: "EventMechanicId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_version_EventTypeId",
                table: "subscription_version",
                column: "EventTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_version_Mode_Order",
                table: "subscription_version",
                columns: new[] { "Mode", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_version_SubscriptionId_Version",
                table: "subscription_version",
                columns: new[] { "SubscriptionId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_version");

            migrationBuilder.DropTable(
                name: "subscription");
        }
    }
}
