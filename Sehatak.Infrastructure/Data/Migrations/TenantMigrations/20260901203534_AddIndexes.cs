using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class AddIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) تحويل نوع العمود أولًا (آمن للتكرار — لو already varchar ما بيصير خطأ)
            migrationBuilder.AlterColumn<string>(
                name: "appointmentStatus",
                table: "appointments",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            // 2) إنشاء الـ indexes الجديدة، بس إذا مش موجودة أصلاً
            migrationBuilder.Sql(@"
        SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'doctor_ratings'
            AND index_name = 'IX_doctor_ratings_DoctorId_PatientId');
        SET @sql := IF(@exist = 0,
            'CREATE INDEX IX_doctor_ratings_DoctorId_PatientId ON doctor_ratings (DoctorId, PatientId)',
            'SELECT 1');
        PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
    ");

            migrationBuilder.Sql(@"
        SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'appointments'
            AND index_name = 'IX_appointments_doctorId_appointmentDate');
        SET @sql := IF(@exist = 0,
            'CREATE INDEX IX_appointments_doctorId_appointmentDate ON appointments (doctorId, appointmentDate)',
            'SELECT 1');
        PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
    ");

            migrationBuilder.Sql(@"
        SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'appointments'
            AND index_name = 'IX_appointments_patientId_appointmentStatus');
        SET @sql := IF(@exist = 0,
            'CREATE INDEX IX_appointments_patientId_appointmentStatus ON appointments (patientId, appointmentStatus)',
            'SELECT 1');
        PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
    ");

            // 3) حذف القديمة، بس إذا موجودة أصلاً (والجديدة صارت موجودة أكيد من فوق فالـ FK محمي)
            migrationBuilder.Sql(@"
        SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'doctor_ratings'
            AND index_name = 'IX_doctor_ratings_DoctorId');
        SET @sql := IF(@exist > 0,
            'DROP INDEX IX_doctor_ratings_DoctorId ON doctor_ratings',
            'SELECT 1');
        PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
    ");

            migrationBuilder.Sql(@"
        SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'appointments'
            AND index_name = 'IX_appointments_doctorId');
        SET @sql := IF(@exist > 0,
            'DROP INDEX IX_appointments_doctorId ON appointments',
            'SELECT 1');
        PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
    ");

            migrationBuilder.Sql(@"
        SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = 'appointments'
            AND index_name = 'IX_appointments_patientId');
        SET @sql := IF(@exist > 0,
            'DROP INDEX IX_appointments_patientId ON appointments',
            'SELECT 1');
        PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
    ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1) رجّعي القديم أولًا (يغطي الـ FK)
            migrationBuilder.CreateIndex(
                name: "IX_doctor_ratings_DoctorId",
                table: "doctor_ratings",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_doctorId",
                table: "appointments",
                column: "doctorId");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_patientId",
                table: "appointments",
                column: "patientId");

            // 2) هلأ احذفي الجديد (لازم قبل الرجوع بـ longtext لأنه الـ Index لسا شامل هاد العمود)
            migrationBuilder.DropIndex(
                name: "IX_doctor_ratings_DoctorId_PatientId",
                table: "doctor_ratings");

            migrationBuilder.DropIndex(
                name: "IX_appointments_doctorId_appointmentDate",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "IX_appointments_patientId_appointmentStatus",
                table: "appointments");

            // 3) وأخيرًا رجّعي النوع لـ longtext، بعد ما صار العمود مو جوا أي Index
            migrationBuilder.AlterColumn<string>(
                name: "appointmentStatus",
                table: "appointments",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
