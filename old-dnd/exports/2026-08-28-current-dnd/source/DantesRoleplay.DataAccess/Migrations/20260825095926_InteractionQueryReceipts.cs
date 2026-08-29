using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InteractionQueryReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interaction_execution_query_result",
                columns: table => new
                {
                    ExecutionReceiptId = table.Column<string>(type: "TEXT", maxLength: 52, nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposalStepId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    QualifiedId = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    OutputSchemaHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResultFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceRevisionFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Exposure = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OutputJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_execution_query_result", x => new { x.ExecutionReceiptId, x.Ordinal });
                    table.CheckConstraint("CK_interaction_execution_query_result_bounds", "length(\"ProposalStepId\") BETWEEN 1 AND 200 AND length(\"QualifiedId\") BETWEEN 3 AND 400");
                    table.CheckConstraint("CK_interaction_execution_query_result_exposure", "\"Exposure\" IN ('model-visible', 'binding-only')");
                    table.CheckConstraint("CK_interaction_execution_query_result_hashes", "length(\"OutputSchemaHash\") = 64 AND \"OutputSchemaHash\" NOT GLOB '*[^0-9A-F]*' AND length(\"ResultFingerprint\") = 64 AND \"ResultFingerprint\" NOT GLOB '*[^0-9A-F]*' AND length(\"SourceRevisionFingerprint\") = 64 AND \"SourceRevisionFingerprint\" NOT GLOB '*[^0-9A-F]*'");
                    table.CheckConstraint("CK_interaction_execution_query_result_ordinal", "\"Ordinal\" BETWEEN 1 AND 16");
                    table.CheckConstraint("CK_interaction_execution_query_result_output", "(\"Exposure\" = 'binding-only' AND \"OutputJson\" IS NULL) OR (\"Exposure\" = 'model-visible' AND length(\"OutputJson\") BETWEEN 1 AND 65536 AND json_valid(\"OutputJson\"))");
                    table.ForeignKey(
                        name: "FK_interaction_execution_query_result_interaction_execution_receipt_ExecutionReceiptId",
                        column: x => x.ExecutionReceiptId,
                        principalTable: "interaction_execution_receipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_execution_query_result_ExecutionReceiptId_ProposalStepId",
                table: "interaction_execution_query_result",
                columns: new[] { "ExecutionReceiptId", "ProposalStepId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interaction_execution_query_result");
        }
    }
}
