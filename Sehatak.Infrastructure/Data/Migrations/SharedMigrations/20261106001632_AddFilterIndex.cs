using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.SharedMigrations
{
    /// <inheritdoc />
    public partial class AddFilterIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subscription_payments_RequestId",
                table: "subscription_payments");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_RequestId",
                table: "subscription_payments",
                column: "RequestId",
                unique: true,
                filter: "[RequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subscription_payments_RequestId",
                table: "subscription_payments");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_RequestId",
                table: "subscription_payments",
                column: "RequestId");
        }
    }
}
