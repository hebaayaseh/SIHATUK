using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.SharedMigrations
{
    /// <inheritdoc />
    public partial class AddRequestIdToSubscriptionPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SubscriptionId",
                table: "subscription_payments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "CenterId",
                table: "subscription_payments",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "RequestId",
                table: "subscription_payments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payments_RequestId",
                table: "subscription_payments",
                column: "RequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_subscription_payments_Center_Registration_Request_RequestId",
                table: "subscription_payments",
                column: "RequestId",
                principalTable: "Center_Registration_Request",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscription_payments_Center_Registration_Request_RequestId",
                table: "subscription_payments");

            migrationBuilder.DropIndex(
                name: "IX_subscription_payments_RequestId",
                table: "subscription_payments");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "subscription_payments");

            migrationBuilder.AlterColumn<int>(
                name: "SubscriptionId",
                table: "subscription_payments",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CenterId",
                table: "subscription_payments",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
