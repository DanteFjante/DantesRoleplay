using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InteractionRecipeParameterizedReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReplayFallbackReason",
                table: "interaction_recipe_evidence",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "none");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplayFallbackReason",
                table: "interaction_recipe_evidence");
        }
    }
}
