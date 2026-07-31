using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class RemovePaymentIdFromConsultation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_consultations_payments_PaymentId",
                table: "consultations");

            migrationBuilder.DropIndex(
                name: "IX_consultations_PaymentId",
                table: "consultations");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "consultations");

            migrationBuilder.CreateIndex(
                name: "IX_payments_ConsultationId",
                table: "payments",
                column: "ConsultationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_payments_consultations_ConsultationId",
                table: "payments",
                column: "ConsultationId",
                principalTable: "consultations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payments_consultations_ConsultationId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_ConsultationId",
                table: "payments");

            migrationBuilder.AddColumn<int>(
                name: "PaymentId",
                table: "consultations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_consultations_PaymentId",
                table: "consultations",
                column: "PaymentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_consultations_payments_PaymentId",
                table: "consultations",
                column: "PaymentId",
                principalTable: "payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
