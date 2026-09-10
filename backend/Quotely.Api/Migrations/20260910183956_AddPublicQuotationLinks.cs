using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotely.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicQuotationLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublicLinkCreatedAt",
                table: "Quotations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicTokenHash",
                table: "Quotations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAt",
                table: "Quotations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RespondedByEmail",
                table: "Quotations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RespondedByName",
                table: "Quotations",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseComment",
                table: "Quotations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_PublicTokenHash",
                table: "Quotations",
                column: "PublicTokenHash",
                unique: true,
                filter: "[PublicTokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotations_PublicTokenHash",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "PublicLinkCreatedAt",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "PublicTokenHash",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "RespondedAt",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "RespondedByEmail",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "RespondedByName",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "ResponseComment",
                table: "Quotations");
        }
    }
}
