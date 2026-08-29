using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InteractionRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecipeId",
                table: "interaction_resolution_receipt",
                type: "TEXT",
                maxLength: 102,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipeTemplateFingerprint",
                table: "interaction_resolution_receipt",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecipeVersion",
                table: "interaction_resolution_receipt",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "interaction_recipe",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 102, nullable: false),
                    ApplicationId = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    TemplateFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TemplateJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_recipe", x => x.Id);
                    table.CheckConstraint("CK_interaction_recipe_application", "length(\"ApplicationId\") BETWEEN 1 AND 63 AND \"ApplicationId\" <> 'system'");
                    table.CheckConstraint("CK_interaction_recipe_hash", "length(\"TemplateFingerprint\") = 64 AND \"TemplateFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_interaction_recipe_id", "length(\"Id\") BETWEEN 41 AND 102 AND \"Id\" GLOB '*.recipe.[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]' ");
                    table.CheckConstraint("CK_interaction_recipe_template", "length(\"TemplateJson\") BETWEEN 2 AND 65536 AND json_valid(\"TemplateJson\") AND json_type(\"TemplateJson\") = 'object'");
                });

            migrationBuilder.CreateTable(
                name: "interaction_recipe_evidence",
                columns: table => new
                {
                    RecipeId = table.Column<string>(type: "TEXT", maxLength: 102, nullable: false),
                    ExecutionReceiptId = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResolutionReceiptId = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    IntentText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IntentFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RoleProfile = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_recipe_evidence", x => new { x.RecipeId, x.ExecutionReceiptId, x.Kind });
                    table.CheckConstraint("CK_interaction_recipe_evidence_bounds", "length(\"RecipeId\") BETWEEN 41 AND 102 AND length(\"ExecutionReceiptId\") = 52 AND length(\"ResolutionReceiptId\") = 52 AND length(\"IntentText\") <= 500 AND length(\"RoleProfile\") BETWEEN 1 AND 300");
                    table.CheckConstraint("CK_interaction_recipe_evidence_hash", "length(\"IntentFingerprint\") = 64 AND \"IntentFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_interaction_recipe_evidence_kind", "\"Kind\" IN ('derived', 'use-success', 'use-failure')");
                    table.ForeignKey(
                        name: "FK_interaction_recipe_evidence_interaction_execution_receipt_ExecutionReceiptId",
                        column: x => x.ExecutionReceiptId,
                        principalTable: "interaction_execution_receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_interaction_recipe_evidence_interaction_recipe_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "interaction_recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_interaction_recipe_evidence_interaction_resolution_receipt_ResolutionReceiptId",
                        column: x => x.ResolutionReceiptId,
                        principalTable: "interaction_resolution_receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "interaction_recipe_revision",
                columns: table => new
                {
                    RecipeId = table.Column<string>(type: "TEXT", maxLength: 102, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ApplicationRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EffectiveSetFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReviewerPrincipalReference = table.Column<string>(type: "TEXT", maxLength: 74, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RequestToken = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_recipe_revision", x => new { x.RecipeId, x.Version });
                    table.CheckConstraint("CK_interaction_recipe_revision_bounds", "length(\"RecipeId\") BETWEEN 41 AND 102 AND length(\"ReviewerPrincipalReference\") <= 74 AND length(\"Reason\") <= 1000 AND length(\"RequestToken\") BETWEEN 1 AND 128");
                    table.CheckConstraint("CK_interaction_recipe_revision_hashes", "length(\"ApplicationFingerprint\") = 64 AND \"ApplicationFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"EffectiveSetFingerprint\") = 64 AND \"EffectiveSetFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"RequestFingerprint\") = 64 AND \"RequestFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_interaction_recipe_revision_status", "\"Status\" IN ('candidate', 'verified', 'stale', 'retired')");
                    table.CheckConstraint("CK_interaction_recipe_revision_version", "\"Version\" > 0 AND \"ApplicationRevision\" > 0");
                    table.ForeignKey(
                        name: "FK_interaction_recipe_revision_interaction_recipe_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "interaction_recipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_recipe_ApplicationId_CreatedAtUtc_Id",
                table: "interaction_recipe",
                columns: new[] { "ApplicationId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_recipe_ApplicationId_TemplateFingerprint",
                table: "interaction_recipe",
                columns: new[] { "ApplicationId", "TemplateFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_interaction_recipe_evidence_ExecutionReceiptId",
                table: "interaction_recipe_evidence",
                column: "ExecutionReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_interaction_recipe_evidence_ResolutionReceiptId",
                table: "interaction_recipe_evidence",
                column: "ResolutionReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_interaction_recipe_revision_ReviewerPrincipalReference_RequestToken",
                table: "interaction_recipe_revision",
                columns: new[] { "ReviewerPrincipalReference", "RequestToken" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interaction_recipe_evidence");

            migrationBuilder.DropTable(
                name: "interaction_recipe_revision");

            migrationBuilder.DropTable(
                name: "interaction_recipe");

            migrationBuilder.DropColumn(
                name: "RecipeId",
                table: "interaction_resolution_receipt");

            migrationBuilder.DropColumn(
                name: "RecipeTemplateFingerprint",
                table: "interaction_resolution_receipt");

            migrationBuilder.DropColumn(
                name: "RecipeVersion",
                table: "interaction_resolution_receipt");
        }
    }
}
