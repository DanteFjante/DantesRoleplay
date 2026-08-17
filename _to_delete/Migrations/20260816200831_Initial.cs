using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Tool = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Intent = table.Column<string>(type: "TEXT", nullable: false),
                    ProceduresUsed = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "procedure_contract",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_contract", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "procedure_contract_version",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContractId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: false),
                    Constraints = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ChangeNote = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_contract_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_procedure_contract_version_procedure_contract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "procedure_contract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_operation_Timestamp",
                table: "operation",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_contract_Category",
                table: "procedure_contract",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_contract_Status",
                table: "procedure_contract",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_contract_version_ContractId_Version",
                table: "procedure_contract_version",
                columns: new[] { "ContractId", "Version" },
                unique: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation");

            migrationBuilder.DropTable(
                name: "procedure_contract_version");

            migrationBuilder.DropTable(
                name: "procedure_relation");

            migrationBuilder.DropTable(
                name: "procedure_contract");
        }
    }
}
