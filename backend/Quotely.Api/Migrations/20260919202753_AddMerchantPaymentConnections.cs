using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotely.Api.Migrations
{
    /// <summary>
    /// V2.7 — a business's own payment account.
    ///
    /// Additive and forward-only. Nothing existing is dropped: the one altered column widens
    /// WebhookEvents.EventId from 120 to 180 characters to hold the connection-scoped idempotency
    /// key, which cannot lose data because it is a widening. Existing payments keep a null
    /// MerchantConnectionId, which is the truthful record — they were collected before there was
    /// more than one account to collect into.
    /// </summary>
    public partial class AddMerchantPaymentConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                table: "WebhookEvents",
                type: "nvarchar(180)",
                maxLength: 180,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(120)",
                oldMaxLength: 120);

            migrationBuilder.AddColumn<Guid>(
                name: "MerchantConnectionId",
                table: "WebhookEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "WebhookEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MerchantConnectionId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MerchantPaymentConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    ProviderAccountId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PublicKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    KeySecretCipher = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    AccessTokenCipher = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    RefreshTokenCipher = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WebhookSecretCipher = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    WebhookRouteToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OauthStateHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OauthStateExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StatusMessage = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastVerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantPaymentConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantPaymentConnections_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_UserId_ReceivedAt",
                table: "WebhookEvents",
                columns: new[] { "UserId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MerchantConnectionId",
                table: "Payments",
                column: "MerchantConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantPaymentConnections_Provider_Status",
                table: "MerchantPaymentConnections",
                columns: new[] { "Provider", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantPaymentConnections_UserId_Provider",
                table: "MerchantPaymentConnections",
                columns: new[] { "UserId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantPaymentConnections_WebhookRouteToken",
                table: "MerchantPaymentConnections",
                column: "WebhookRouteToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MerchantPaymentConnections");

            migrationBuilder.DropIndex(
                name: "IX_WebhookEvents_UserId_ReceivedAt",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_Payments_MerchantConnectionId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "MerchantConnectionId",
                table: "WebhookEvents");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "WebhookEvents");

            migrationBuilder.DropColumn(
                name: "MerchantConnectionId",
                table: "Payments");

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                table: "WebhookEvents",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(180)",
                oldMaxLength: 180);
        }
    }
}
