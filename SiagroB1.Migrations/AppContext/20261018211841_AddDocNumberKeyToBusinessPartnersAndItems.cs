using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddDocNumberKeyToBusinessPartnersAndItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DocNumberKey",
                table: "ITEMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocNumberKey",
                table: "BUSINESS_PARTNERS",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocNumberKey",
                table: "ITEMS");

            migrationBuilder.DropColumn(
                name: "DocNumberKey",
                table: "BUSINESS_PARTNERS");
        }
    }
}
