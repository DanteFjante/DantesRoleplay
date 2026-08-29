using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations;

/// <summary>Stores the optional, bounded event-payload-to-reaction-role binding per subscription version.</summary>
[DbContext(typeof(DantesRoleplayDbContext))]
[Migration("20260821150000_PayloadRoleBindings")]
public partial class PayloadRoleBindings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RoleFromEventPayloadJson",
            table: "subscription_version",
            type: "TEXT",
            nullable: false,
            defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RoleFromEventPayloadJson",
            table: "subscription_version");
    }
}
