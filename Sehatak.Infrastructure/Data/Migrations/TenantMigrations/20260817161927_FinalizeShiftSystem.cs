using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class FinalizeShiftSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            

            migrationBuilder.Sql(
                "ALTER TABLE `staff_shifts` CHANGE COLUMN `staffId` `UserId` int(11) NOT NULL;");

            migrationBuilder.Sql(
                "ALTER TABLE `staff_shifts` DROP INDEX `IX_staff_shifts_staffId`, ADD INDEX `IX_staff_shifts_UserId` (`UserId`);");


            migrationBuilder.Sql(
                "ALTER TABLE `staff_attendance` CHANGE COLUMN `StaffId` `UserId` int(11) NOT NULL;");

            migrationBuilder.Sql(
                "ALTER TABLE `staff_attendance` DROP INDEX `IX_staff_attendance_StaffId_AttendanceDate`, ADD INDEX `IX_staff_attendance_UserId_AttendanceDate` (`UserId`, `AttendanceDate`);");

            migrationBuilder.AlterColumn<int>(
                name: "ShiftName",
                table: "staff_shifts",
                type: "int",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldMaxLength: 100)
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ShiftDate",
                table: "staff_shifts",
                type: "varchar(10)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddForeignKey(
                name: "FK_staff_attendance_users_UserId",
                table: "staff_attendance",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_staff_shifts_users_UserId",
                table: "staff_shifts",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_staff_attendance_users_UserId", table: "staff_attendance");
            migrationBuilder.DropForeignKey(name: "FK_staff_shifts_users_UserId", table: "staff_shifts");

            migrationBuilder.DropColumn(name: "ShiftDate", table: "staff_shifts");

            migrationBuilder.Sql(
                "ALTER TABLE `staff_shifts` CHANGE COLUMN `UserId` `staffId` int(11) NOT NULL;");

            migrationBuilder.Sql(
                "ALTER TABLE `staff_shifts` DROP INDEX `IX_staff_shifts_UserId`, ADD INDEX `IX_staff_shifts_staffId` (`staffId`);");

            migrationBuilder.Sql(
                "ALTER TABLE `staff_attendance` CHANGE COLUMN `UserId` `StaffId` int(11) NOT NULL;");

            migrationBuilder.Sql(
                "ALTER TABLE `staff_attendance` DROP INDEX `IX_staff_attendance_UserId_AttendanceDate`, ADD INDEX `IX_staff_attendance_StaffId_AttendanceDate` (`StaffId`, `AttendanceDate`);");

            migrationBuilder.AlterColumn<string>(
                name: "ShiftName",
                table: "staff_shifts",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldMaxLength: 100)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DayOfWeek",
                table: "staff_shifts",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "staff_shifts",
                type: "time(6)",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "staff_shifts",
                type: "time(6)",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddForeignKey(
                name: "FK_staff_attendance_users_StaffId",
                table: "staff_attendance",
                column: "StaffId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_staff_shifts_users_staffId",
                table: "staff_shifts",
                column: "staffId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
