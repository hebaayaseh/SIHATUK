using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class AddChangesToTenant5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_doctors_service_prices_ConsultationCostId",
                table: "doctors");

            migrationBuilder.AddColumn<bool>(
                name: "IsFollowUp",
                table: "appointments",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_doctors_service_prices_ConsultationCostId",
                table: "doctors",
                column: "ConsultationCostId",
                principalTable: "service_prices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_doctors_service_prices_ConsultationCostId",
                table: "doctors");

            migrationBuilder.DropColumn(
                name: "IsFollowUp",
                table: "appointments");

            migrationBuilder.AddForeignKey(
                name: "FK_doctors_service_prices_ConsultationCostId",
                table: "doctors",
                column: "ConsultationCostId",
                principalTable: "service_prices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
