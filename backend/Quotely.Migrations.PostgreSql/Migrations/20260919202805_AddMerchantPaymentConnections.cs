using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotely.Migrations.PostgreSql.Migrations
{
    /// <summary>
    /// V2.7 — a business's own payment account, for PostgreSQL.
    ///
    /// The same model as the SQL Server migration, different DDL. Note what is absent: the
    /// filtered "WHERE ... IS NOT NULL" predicates the SQL Server unique indexes carry. PostgreSQL
    /// already treats NULLs as distinct in a unique index, so a plain one is equivalent there.
    /// </summary>
    public partial class AddMerchantPaymentConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                table: "WebhookEvents",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AddColumn<Guid>(
                name: "MerchantConnectionId",
                table: "WebhookEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "WebhookEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MerchantConnectionId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MerchantPaymentConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PublicKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    KeySecretCipher = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AccessTokenCipher = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    RefreshTokenCipher = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WebhookSecretCipher = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    WebhookRouteToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OauthStateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OauthStateExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StatusMessage = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
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
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(180)",
                oldMaxLength: 180);
        }
    }
}
