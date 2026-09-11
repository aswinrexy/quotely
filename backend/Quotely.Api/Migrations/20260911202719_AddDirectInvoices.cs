using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotely.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_QuotationId",
                table: "Invoices");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuotationId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuotationId",
                table: "Invoices",
                column: "QuotationId",
                unique: true,
                filter: "[QuotationId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_QuotationId",
                table: "Invoices");

            migrationBuilder.AlterColumn<Guid>(
                name: "QuotationId",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuotationId",
                table: "Invoices",
                column: "QuotationId",
                unique: true);
        }
    }
}
