using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DantesRoleplay.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "component_definition",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Schema = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_component_definition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "entity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "operation",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Tool = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Intent = table.Column<string>(type: "TEXT", nullable: false),
                    ProceduresCited = table.Column<string>(type: "TEXT", nullable: false),
                    ProceduresRead = table.Column<string>(type: "TEXT", nullable: false),
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
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_contract", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "component",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_component", x => x.Id);
                    table.ForeignKey(
                        name: "FK_component_component_definition_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "component_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_component_entity_EntityId",
                        column: x => x.EntityId,
                        principalTable: "entity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "containment",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContainerId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContainedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slot = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_containment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_containment_entity_ContainedId",
                        column: x => x.ContainedId,
                        principalTable: "entity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_containment_entity_ContainerId",
                        column: x => x.ContainerId,
                        principalTable: "entity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "relationship",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromEntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ToEntityId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relationship", x => x.Id);
                    table.ForeignKey(
                        name: "FK_relationship_entity_FromEntityId",
                        column: x => x.FromEntityId,
                        principalTable: "entity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_relationship_entity_ToEntityId",
                        column: x => x.ToEntityId,
                        principalTable: "entity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    Governs = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: false),
                    Constraints = table.Column<string>(type: "TEXT", nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ChangeNote = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                name: "IX_component_DefinitionId",
                table: "component",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_component_EntityId_DefinitionId",
                table: "component",
                columns: new[] { "EntityId", "DefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_containment_ContainedId",
                table: "containment",
                column: "ContainedId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_containment_ContainerId",
                table: "containment",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_entity_DeletedAt",
                table: "entity",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_entity_Name",
                table: "entity",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_operation_Subject",
                table: "operation",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_operation_Timestamp",
                table: "operation",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_operation_Tool_Timestamp",
                table: "operation",
                columns: new[] { "Tool", "Timestamp" });

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

            migrationBuilder.CreateIndex(
                name: "IX_relationship_FromEntityId_ToEntityId_Kind",
                table: "relationship",
                columns: new[] { "FromEntityId", "ToEntityId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_relationship_ToEntityId",
                table: "relationship",
                column: "ToEntityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "component");

            migrationBuilder.DropTable(
                name: "containment");

            migrationBuilder.DropTable(
                name: "operation");

            migrationBuilder.DropTable(
                name: "procedure_contract_version");

            migrationBuilder.DropTable(
                name: "procedure_relation");

            migrationBuilder.DropTable(
                name: "relationship");

            migrationBuilder.DropTable(
                name: "component_definition");

            migrationBuilder.DropTable(
                name: "procedure_contract");

            migrationBuilder.DropTable(
                name: "entity");
        }
    }
}
