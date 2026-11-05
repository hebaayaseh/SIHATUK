using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.SharedMigrations
{
    /// <inheritdoc />
    public partial class AddIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subscription_payments_SubscriptionId",
                table: "subscription_payments");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_SubscriptionId",
                table: "subscription_payments",
                column: "SubscriptionId",
                unique: true,
                filter: "[SubscriptionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subscription_payments_SubscriptionId",
                table: "subscription_payments");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_SubscriptionId",
                table: "subscription_payments",
                column: "SubscriptionId");
        }
    }
}
