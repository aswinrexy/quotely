using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotely.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddImportExportInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TradeInvoiceDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TradeType = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    BuyerOrderNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    BuyerOrderDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OtherReferences = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    PartyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PartyAddress = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    ConsigneeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConsigneeAddress = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    BuyerSameAsConsignee = table.Column<bool>(type: "boolean", nullable: false),
                    BuyerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BuyerAddress = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    NotifyPartyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NotifyPartyAddress = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    PreCarriageBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PlaceOfReceipt = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    VesselOrFlightNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PortOfLoading = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PortOfDischarge = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    FinalDestination = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CountryOfOrigin = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CountryOfFinalDestination = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TermsOfDelivery = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TermsOfPayment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PricingTerm = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IecNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    GstNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PanNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ApedaRegistrationNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ApedaValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    HeaderDeclarations = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FooterDeclaration = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AuthorisedSignatory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TotalNetWeight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    TotalGrossWeight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    TotalPackages = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    WeightUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeInvoiceDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeInvoiceDetails_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeLineDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarksAndNumbers = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Dimension = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    HsCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NetWeight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    GrossWeight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    QuantityUnit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    RateBasis = table.Column<int>(type: "integer", nullable: false),
                    RateLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeLineDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeLineDetails_InvoiceItems_InvoiceItemId",
                        column: x => x.InvoiceItemId,
                        principalTable: "InvoiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IecNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    GstNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PanNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ApedaRegistrationNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ApedaValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    PartyNameOverride = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PartyAddressOverride = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    DefaultCountryOfOrigin = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DefaultTermsOfDelivery = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DefaultTermsOfPayment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DefaultPricingTerm = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DefaultPortOfLoading = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    DefaultPreCarriageBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DefaultHeaderDeclarations = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DefaultFooterDeclaration = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DefaultAuthorisedSignatory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DefaultCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId_Type",
                table: "Invoices",
                columns: new[] { "UserId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoiceDetails_InvoiceId",
                table: "TradeInvoiceDetails",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeLineDetails_InvoiceItemId",
                table: "TradeLineDetails",
                column: "InvoiceItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeProfiles_UserId",
                table: "TradeProfiles",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeInvoiceDetails");

            migrationBuilder.DropTable(
                name: "TradeLineDetails");

            migrationBuilder.DropTable(
                name: "TradeProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_UserId_Type",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Invoices");
        }
    }
}
