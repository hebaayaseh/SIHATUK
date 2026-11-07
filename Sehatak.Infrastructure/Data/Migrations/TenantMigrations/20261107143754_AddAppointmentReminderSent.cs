using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class AddAppointmentReminderSent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DelayProcessed",
                table: "appointments",
                newName: "ReminderSent");

            migrationBuilder.AddColumn<bool>(
                name: "IsTimeCompensation",
                table: "appointments",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTimeCompensation",
                table: "appointments");

            migrationBuilder.RenameColumn(
                name: "ReminderSent",
                table: "appointments",
                newName: "DelayProcessed");
        }
    }
}
