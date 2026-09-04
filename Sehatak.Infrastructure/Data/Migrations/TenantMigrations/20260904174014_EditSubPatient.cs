using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class EditSubPatient : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) حذف FK القديم — بس إذا موجود فعلاً
            migrationBuilder.Sql(@"
                SET @exist := (SELECT COUNT(1) FROM information_schema.TABLE_CONSTRAINTS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'patients'
                    AND CONSTRAINT_NAME = 'FK_patients_patients_SubPatientId'
                    AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@exist > 0,
                    'ALTER TABLE patients DROP FOREIGN KEY FK_patients_patients_SubPatientId',
                    'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
            ");

            // 2) فهرس مؤقت يغطي DoctorId قبل حذف المركّب (يحمي من مشكلة FK على doctor_ratings)
            migrationBuilder.Sql(@"
                SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
                    WHERE table_schema = DATABASE() AND table_name = 'doctor_ratings'
                    AND index_name = 'IX_doctor_ratings_DoctorId_temp');
                SET @sql := IF(@exist = 0,
                    'CREATE INDEX IX_doctor_ratings_DoctorId_temp ON doctor_ratings (DoctorId)',
                    'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
            ");

            // 3) حذف المركّب القديم — بس إذا موجود
            migrationBuilder.Sql(@"
                SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
                    WHERE table_schema = DATABASE() AND table_name = 'doctor_ratings'
                    AND index_name = 'IX_doctor_ratings_DoctorId_PatientId');
                SET @sql := IF(@exist > 0,
                    'DROP INDEX IX_doctor_ratings_DoctorId_PatientId ON doctor_ratings',
                    'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
            ");

            // 4) rename SubPatientName → LastName (بجلب نوع العمود ديناميكيًا لتوافق CHANGE COLUMN)
            migrationBuilder.Sql(@"
    SET @exist := (SELECT COUNT(1) FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = 'patients'
        AND column_name = 'SubPatientName');
    SET @coltype := (SELECT COLUMN_TYPE FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = 'patients'
        AND column_name = 'SubPatientName' LIMIT 1);
    SET @sql := IF(@exist > 0,
        CONCAT('ALTER TABLE patients CHANGE COLUMN SubPatientName LastName ', @coltype, ' NULL'),
        'SELECT 1');
    PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            migrationBuilder.Sql(@"
    SET @exist := (SELECT COUNT(1) FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = 'patients'
        AND column_name = 'SubPatientId');
    SET @coltype := (SELECT COLUMN_TYPE FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = 'patients'
        AND column_name = 'SubPatientId' LIMIT 1);
    SET @sql := IF(@exist > 0,
        CONCAT('ALTER TABLE patients CHANGE COLUMN SubPatientId ParentPatientId ', @coltype, ' NULL'),
        'SELECT 1');
    PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            // 5) استبدال rename index بـ drop + create (أضمن توافقًا مع كل النسخ)
            migrationBuilder.Sql(@"
    SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
        WHERE table_schema = DATABASE() AND table_name = 'patients'
        AND index_name = 'IX_patients_SubPatientId');
    SET @sql := IF(@exist > 0,
        'DROP INDEX IX_patients_SubPatientId ON patients',
        'SELECT 1');
    PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            migrationBuilder.Sql(@"
    SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
        WHERE table_schema = DATABASE() AND table_name = 'patients'
        AND index_name = 'IX_patients_ParentPatientId');
    SET @sql := IF(@exist = 0,
        'CREATE INDEX IX_patients_ParentPatientId ON patients (ParentPatientId)',
        'SELECT 1');
    PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            // 6) إضافة FirstName — بس إذا مش موجود أصلًا
            migrationBuilder.Sql(@"
                SET @exist := (SELECT COUNT(1) FROM information_schema.columns
                    WHERE table_schema = DATABASE() AND table_name = 'patients'
                    AND column_name = 'FirstName');
                SET @sql := IF(@exist = 0,
                    'ALTER TABLE patients ADD COLUMN FirstName VARCHAR(200) NULL',
                    'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
            ");

            // 7) إعادة إنشاء المركّب (بدون unique) — بس إذا مش موجود
            migrationBuilder.Sql(@"
                SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
                    WHERE table_schema = DATABASE() AND table_name = 'doctor_ratings'
                    AND index_name = 'IX_doctor_ratings_DoctorId_PatientId');
                SET @sql := IF(@exist = 0,
                    'CREATE INDEX IX_doctor_ratings_DoctorId_PatientId ON doctor_ratings (DoctorId, PatientId)',
                    'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
            ");

            // 8) حذف الفهرس المؤقت
            migrationBuilder.Sql(@"
                SET @exist := (SELECT COUNT(1) FROM information_schema.statistics
                    WHERE table_schema = DATABASE() AND table_name = 'doctor_ratings'
                    AND index_name = 'IX_doctor_ratings_DoctorId_temp');
                SET @sql := IF(@exist > 0,
                    'DROP INDEX IX_doctor_ratings_DoctorId_temp ON doctor_ratings',
                    'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
            ");

            // 9) إضافة FK الجديد — بس إذا مش موجود أصلًا
            migrationBuilder.Sql(@"
                SET @exist := (SELECT COUNT(1) FROM information_schema.TABLE_CONSTRAINTS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'patients'
                    AND CONSTRAINT_NAME = 'FK_patients_patients_ParentPatientId'
                    AND CONSTRAINT_TYPE = 'FOREIGN KEY');
                SET @sql := IF(@exist = 0,
                    'ALTER TABLE patients ADD CONSTRAINT FK_patients_patients_ParentPatientId FOREIGN KEY (ParentPatientId) REFERENCES patients (patientId) ON DELETE RESTRICT',
                    'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // نفس المبدأ بالعكس — اختياري تكتبيها الآن، أو تأجليها لحد ما تتأكدي الـ Up تمام
            throw new NotImplementedException("Down لسا ما انبنت — بلشي فيها بعد ما تتأكدي إنه Up اشتغلت صح على كل التينانتس");
        }
    }
}