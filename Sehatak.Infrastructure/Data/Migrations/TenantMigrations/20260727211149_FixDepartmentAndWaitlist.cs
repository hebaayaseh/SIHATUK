using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class FixDepartmentAndWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. احذفي الـ FK القديم (بسياسة SET NULL) أولاً
            migrationBuilder.DropForeignKey(
                name: "FK_doctors_departments_departmentId",
                table: "doctors");

            // 2. هلق خلي departmentId إلزامي (بعد ما زال القيد القديم)
            migrationBuilder.Sql(
                "ALTER TABLE `doctors` MODIFY COLUMN `departmentId` int(11) NOT NULL;");

            // 3. أضيفي FK جديد بسياسة Restrict
            migrationBuilder.AddForeignKey(
                name: "FK_doctors_departments_departmentId",
                table: "doctors",
                column: "departmentId",
                principalTable: "departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

           
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_doctors_departments_departmentId",
                table: "doctors");

            migrationBuilder.Sql(
                "ALTER TABLE `doctors` MODIFY COLUMN `departmentId` int(11) NULL;");

            migrationBuilder.AddForeignKey(
                name: "FK_doctors_departments_departmentId",
                table: "doctors",
                column: "departmentId",
                principalTable: "departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "PreferredTimeSlot",
                table: "waitlists",
                type: "time(6)",
                nullable: true);
        }
    }
}
