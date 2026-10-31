using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class AddIndexFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_follow_ups_OriginalAppointmentId",
                table: "follow_ups");

            migrationBuilder.CreateIndex(
                name: "IX_follow_ups_OriginalAppointmentId",
                table: "follow_ups",
                column: "OriginalAppointmentId",
                unique: true,
                filter: "[Status] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_follow_ups_OriginalAppointmentId",
                table: "follow_ups");

            migrationBuilder.CreateIndex(
                name: "IX_follow_ups_OriginalAppointmentId",
                table: "follow_ups",
                column: "OriginalAppointmentId");
        }
    }
}
