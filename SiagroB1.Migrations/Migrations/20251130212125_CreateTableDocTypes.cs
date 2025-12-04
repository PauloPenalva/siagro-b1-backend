using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableDocTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NUMBER_SEQUENCE");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DOC_TYPES",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TransactionCode = table.Column<int>(type: "int", nullable: false),
                    Serie = table.Column<string>(type: "VARCHAR(3)", nullable: false),
                    FirstNumber = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DOC_TYPES", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS",
                columns: new[] { "Code", "Sequence" },
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_DocTypeCode",
                table: "PURCHASE_CONTRACTS",
                column: "DocTypeCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_DOC_TYPES_DocTypeCode",
                table: "PURCHASE_CONTRACTS",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_DOC_TYPES_DocTypeCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropTable(
                name: "DOC_TYPES");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_DocTypeCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "NUMBER_SEQUENCE",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    FirstNumber = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false),
                    Serie = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    TransactionCode = table.Column<string>(type: "VARCHAR(15)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NUMBER_SEQUENCE", x => x.Key);
                    table.ForeignKey(
                        name: "FK_NUMBER_SEQUENCE_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_Code_Sequence",
                table: "PURCHASE_CONTRACTS",
                columns: new[] { "Code", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NUMBER_SEQUENCE_BranchKey_TransactionCode_Serie",
                table: "NUMBER_SEQUENCE",
                columns: new[] { "BranchKey", "TransactionCode", "Serie" },
                unique: true);
        }
    }
}
