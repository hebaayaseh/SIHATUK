using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class AddChangesToTenant6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEmergency",
                table: "appointments");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "lab_request_items",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckOutTime",
                table: "appointments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "emergency_cases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PatientName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DoctorUserId = table.Column<int>(type: "int", nullable: false),
                    ReceptionistId = table.Column<int>(type: "int", nullable: false),
                    IsInsurance = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emergency_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_emergency_cases_users_DoctorUserId",
                        column: x => x.DoctorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_emergency_cases_users_ReceptionistId",
                        column: x => x.ReceptionistId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_emergency_cases_DoctorUserId",
                table: "emergency_cases",
                column: "DoctorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_emergency_cases_ReceptionistId",
                table: "emergency_cases",
                column: "ReceptionistId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emergency_cases");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "lab_request_items");

            migrationBuilder.DropColumn(
                name: "CheckOutTime",
                table: "appointments");

            migrationBuilder.AddColumn<bool>(
                name: "IsEmergency",
                table: "appointments",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
