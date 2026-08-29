using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DropProcedureRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "procedure_relation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "procedure_relation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromContractId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ToContractId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_relation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_procedure_relation_procedure_contract_FromContractId",
                        column: x => x.FromContractId,
                        principalTable: "procedure_contract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_procedure_relation_procedure_contract_ToContractId",
                        column: x => x.ToContractId,
                        principalTable: "procedure_contract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_procedure_relation_FromContractId_ToContractId_Kind",
                table: "procedure_relation",
                columns: new[] { "FromContractId", "ToContractId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procedure_relation_ToContractId",
                table: "procedure_relation",
                column: "ToContractId");
        }
    }
}
