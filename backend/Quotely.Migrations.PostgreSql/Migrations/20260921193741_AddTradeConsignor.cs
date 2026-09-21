using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotely.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeConsignor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ConsignorSameAsParty is backfilled TRUE rather than EF's generated false. Every trade
            // document that exists was raised by a business shipping its own goods — there was no
            // way to say otherwise — so false would assert a separate consignor that none of them
            // has. The model's own default is true for the same reason.
            migrationBuilder.AddColumn<string>(
                name: "ConsignorAddress",
                table: "TradeInvoiceDetails",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsignorName",
                table: "TradeInvoiceDetails",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConsignorSameAsParty",
                table: "TradeInvoiceDetails",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsignorAddress",
                table: "TradeInvoiceDetails");

            migrationBuilder.DropColumn(
                name: "ConsignorName",
                table: "TradeInvoiceDetails");

            migrationBuilder.DropColumn(
                name: "ConsignorSameAsParty",
                table: "TradeInvoiceDetails");
        }
    }
}
