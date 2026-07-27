using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class date : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AlterColumn<DateTime>(
               name: "ScheduledAt",
               table: "consultations",
               type: "datetime",
               nullable: true,
               oldClrType: typeof(DateTime),
               oldType: "datetime");


        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AlterColumn<DateTime>(
              name: "ScheduledAt",
              table: "consultations",
              type: "datetime",
              nullable: false,
              oldClrType: typeof(DateTime),
              oldType: "datetime");


        }
    }
}
