using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotely.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReservationSlot",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReservationSlot",
                table: "Payments",
                column: "ReservationSlot",
                unique: true,
                filter: "[ReservationSlot] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_ReservationSlot",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReservationSlot",
                table: "Payments");
        }
    }
}
