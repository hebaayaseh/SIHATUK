using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class AddConsultationToMedicalRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsultationId",
                table: "medical_records",
                type: "int",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_medical_records_consultations_ConsultationId",
                table: "medical_records",
                column: "ConsultationId",
                principalTable: "consultations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_medical_records_consultations_ConsultationId",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "ConsultationId",
                table: "medical_records");
        }
    }
}
