using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddManagementViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MANAGEMENT_VIEWS",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MANAGEMENT_VIEWS", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "MANAGEMENT_VIEW_LINES",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    ParentCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    AccountFrom = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    AccountTo = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Sign = table.Column<int>(type: "int", nullable: false),
                    Formula = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    Bold = table.Column<bool>(type: "bit", nullable: false),
                    Hidden = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MANAGEMENT_VIEW_LINES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MANAGEMENT_VIEW_LINES_MANAGEMENT_VIEWS_ViewCode",
                        column: x => x.ViewCode,
                        principalTable: "MANAGEMENT_VIEWS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MANAGEMENT_VIEW_LINES_ViewCode_Code",
                table: "MANAGEMENT_VIEW_LINES",
                columns: new[] { "ViewCode", "Code" },
                unique: true);

            // DRE e Balanço padrão, com as faixas de conta vazias (o contador preenche).
            foreach (var view in new[] { SiagroB1.Domain.Entities.ManagementViewSeed.IncomeStatement(), SiagroB1.Domain.Entities.ManagementViewSeed.BalanceSheet() })
            {
                migrationBuilder.InsertData(
                    table: "MANAGEMENT_VIEWS",
                    columns: ["Code", "Name", "Type", "UpdatedAt", "UpdatedBy"],
                    values: new object[] { view.Code, view.Name, (int)view.Type, new DateTime(2026, 10, 22), view.UpdatedBy });
                foreach (var l in view.Lines)
                    migrationBuilder.InsertData(
                        table: "MANAGEMENT_VIEW_LINES",
                        columns: ["Id", "ViewCode", "Order", "Code", "Description", "ParentCode", "Kind", "AccountFrom", "AccountTo", "Sign", "Formula", "Bold", "Hidden"],
                        values: new object[] { l.Id, view.Code, l.Order, l.Code, l.Description, l.ParentCode, (int)l.Kind, null, null, (int)l.Sign, l.Formula, l.Bold, l.Hidden });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MANAGEMENT_VIEW_LINES");

            migrationBuilder.DropTable(
                name: "MANAGEMENT_VIEWS");
        }
    }
}
