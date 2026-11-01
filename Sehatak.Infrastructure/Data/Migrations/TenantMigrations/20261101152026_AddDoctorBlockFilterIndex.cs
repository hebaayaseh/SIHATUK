using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class AddDoctorBlockFilterIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DoctorBlockedDays_doctorId",
                table: "DoctorBlockedDays");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorBlockedDays_doctorId_date",
                table: "DoctorBlockedDays",
                columns: new[] { "doctorId", "date" },
                unique: true,
                filter: "[timeSlot] IS NULL AND [isBlocked] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DoctorBlockedDays_doctorId_date",
                table: "DoctorBlockedDays");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorBlockedDays_doctorId",
                table: "DoctorBlockedDays",
                column: "doctorId");
        }
    }
}
