using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations;

/// <summary>Stores bounded generic fan-out metadata and indexes its relationship lookup paths.</summary>
[DbContext(typeof(DantesRoleplayDbContext))]
[Migration("20260821160000_FanoutSelectors")]
public partial class FanoutSelectors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FanoutSelectorJson",
            table: "subscription_version",
            type: "TEXT",
            nullable: false,
            defaultValue: "{}");

        migrationBuilder.CreateIndex(
            name: "IX_relationship_FromEntityId_Kind_ToEntityId",
            table: "relationship",
            columns: new[] { "FromEntityId", "Kind", "ToEntityId" });

        migrationBuilder.CreateIndex(
            name: "IX_relationship_ToEntityId_Kind_FromEntityId",
            table: "relationship",
            columns: new[] { "ToEntityId", "Kind", "FromEntityId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_relationship_FromEntityId_Kind_ToEntityId", table: "relationship");
        migrationBuilder.DropIndex(name: "IX_relationship_ToEntityId_Kind_FromEntityId", table: "relationship");
        migrationBuilder.DropColumn(name: "FanoutSelectorJson", table: "subscription_version");
    }
}
