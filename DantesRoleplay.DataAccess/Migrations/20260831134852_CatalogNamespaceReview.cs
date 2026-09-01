using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations;

[DbContext(typeof(DantesRoleplayDbContext))]
[Migration("20260831134852_CatalogNamespaceReview")]
public partial class CatalogNamespaceReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ReviewNote",
            table: "system_catalog_namespace",
            type: "TEXT",
            maxLength: 2000,
            nullable: false,
            defaultValue: "Existing namespace requires review.");

        migrationBuilder.AddColumn<DateTime>(
            name: "ReviewedAtUtc",
            table: "system_catalog_namespace",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReviewStatus",
            table: "system_catalog_namespace",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            defaultValue: "needs-review");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReviewNote", table: "system_catalog_namespace");
        migrationBuilder.DropColumn(name: "ReviewedAtUtc", table: "system_catalog_namespace");
        migrationBuilder.DropColumn(name: "ReviewStatus", table: "system_catalog_namespace");
    }
}
