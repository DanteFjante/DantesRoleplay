using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InteractionRecipeReplayPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReplayActualAiCalls",
                table: "interaction_recipe_evidence",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplayBaselineAiCalls",
                table: "interaction_recipe_evidence",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplayChoiceResolutionMilliseconds",
                table: "interaction_recipe_evidence",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplayElapsedMilliseconds",
                table: "interaction_recipe_evidence",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplayExecutionMilliseconds",
                table: "interaction_recipe_evidence",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplayOutputTokens",
                table: "interaction_recipe_evidence",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplayPromptTokens",
                table: "interaction_recipe_evidence",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplayProposalMilliseconds",
                table: "interaction_recipe_evidence",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReplaySavedAiCalls",
                table: "interaction_recipe_evidence",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_interaction_recipe_evidence_replay_performance_insert"
                BEFORE INSERT ON "interaction_recipe_evidence"
                WHEN NEW."ReplayBaselineAiCalls" NOT BETWEEN 0 AND 16
                  OR NEW."ReplayActualAiCalls" NOT BETWEEN 0 AND 1
                  OR NEW."ReplaySavedAiCalls" < 0
                  OR NEW."ReplayElapsedMilliseconds" < 0
                  OR NEW."ReplayChoiceResolutionMilliseconds" < 0
                  OR NEW."ReplayProposalMilliseconds" < 0
                  OR NEW."ReplayExecutionMilliseconds" < 0
                  OR NEW."ReplayPromptTokens" < 0
                  OR NEW."ReplayOutputTokens" < 0
                BEGIN
                    SELECT RAISE(ABORT, 'invalid interaction recipe replay performance');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_interaction_recipe_evidence_replay_performance_update"
                BEFORE UPDATE OF "ReplayBaselineAiCalls", "ReplayActualAiCalls", "ReplaySavedAiCalls",
                  "ReplayElapsedMilliseconds", "ReplayChoiceResolutionMilliseconds",
                  "ReplayProposalMilliseconds", "ReplayExecutionMilliseconds", "ReplayPromptTokens",
                  "ReplayOutputTokens" ON "interaction_recipe_evidence"
                WHEN NEW."ReplayBaselineAiCalls" NOT BETWEEN 0 AND 16
                  OR NEW."ReplayActualAiCalls" NOT BETWEEN 0 AND 1
                  OR NEW."ReplaySavedAiCalls" < 0
                  OR NEW."ReplayElapsedMilliseconds" < 0
                  OR NEW."ReplayChoiceResolutionMilliseconds" < 0
                  OR NEW."ReplayProposalMilliseconds" < 0
                  OR NEW."ReplayExecutionMilliseconds" < 0
                  OR NEW."ReplayPromptTokens" < 0
                  OR NEW."ReplayOutputTokens" < 0
                BEGIN
                    SELECT RAISE(ABORT, 'invalid interaction recipe replay performance');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_interaction_recipe_evidence_replay_performance_update\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_interaction_recipe_evidence_replay_performance_insert\";");

            migrationBuilder.DropColumn(
                name: "ReplayActualAiCalls",
                table: "interaction_recipe_evidence");

            migrationBuilder.DropColumn(
                name: "ReplayBaselineAiCalls",
                table: "interaction_recipe_evidence");

            migrationBuilder.DropColumn(
                name: "ReplayChoiceResolutionMilliseconds",
                table: "interaction_recipe_evidence");

            migrationBuilder.DropColumn(
                name: "ReplayElapsedMilliseconds",
                table: "interaction_recipe_evidence");

            migrationBuilder.DropColumn(
                name: "ReplayExecutionMilliseconds",
                table: "interaction_recipe_evidence");

            migrationBuilder.DropColumn(
                name: "ReplayOutputTokens",
                table: "interaction_recipe_evidence");

            migrationBuilder.DropColumn(
                name: "ReplayPromptTokens",
                table: "interaction_recipe_evidence");

            migrationBuilder.DropColumn(
                name: "ReplayProposalMilliseconds",
                table: "interaction_recipe_evidence");

            migrationBuilder.DropColumn(
                name: "ReplaySavedAiCalls",
                table: "interaction_recipe_evidence");
        }
    }
}
