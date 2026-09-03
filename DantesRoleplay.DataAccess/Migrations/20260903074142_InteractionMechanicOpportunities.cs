using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InteractionMechanicOpportunities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interaction_mechanic_opportunity",
                columns: table => new
                {
                    RecipeId = table.Column<string>(type: "TEXT", maxLength: 102, nullable: false),
                    RecipeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipeTemplateFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    ProposalFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProposalJson = table.Column<string>(type: "TEXT", maxLength: 131072, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_mechanic_opportunity", x => x.RecipeId);
                    table.CheckConstraint("CK_interaction_mechanic_opportunity_application", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system'");
                    table.CheckConstraint("CK_interaction_mechanic_opportunity_hashes", "length(\"RecipeTemplateFingerprint\") = 64 AND \"RecipeTemplateFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"ProposalFingerprint\") = 64 AND \"ProposalFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_interaction_mechanic_opportunity_proposal", "length(\"ProposalJson\") BETWEEN 2 AND 131072 AND json_valid(\"ProposalJson\") AND json_type(\"ProposalJson\") = 'object'");
                    table.CheckConstraint("CK_interaction_mechanic_opportunity_recipe", "length(\"RecipeId\") BETWEEN 41 AND 102 AND \"RecipeVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_interaction_mechanic_opportunity_interaction_recipe_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "interaction_recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_mechanic_opportunity_ApplicationId_CreatedAtUtc_RecipeId",
                table: "interaction_mechanic_opportunity",
                columns: new[] { "ApplicationId", "CreatedAtUtc", "RecipeId" });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_mechanic_opportunity_ProposalFingerprint",
                table: "interaction_mechanic_opportunity",
                column: "ProposalFingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interaction_mechanic_opportunity");
        }
    }
}
