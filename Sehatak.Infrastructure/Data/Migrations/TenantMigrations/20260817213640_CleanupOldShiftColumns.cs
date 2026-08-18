using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class CleanupOldShiftColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
            SET @dbname = DATABASE();
            SET @tablename = 'staff_shifts';

            SET @columnname = 'DayOfWeek';
            SET @preparedStatement = (SELECT IF(
                (SELECT COUNT(*) FROM information_schema.columns
                 WHERE table_schema = @dbname AND table_name = @tablename AND column_name = @columnname) > 0,
                'ALTER TABLE staff_shifts DROP COLUMN DayOfWeek',
                'SELECT 1'
            ));
            PREPARE stmt FROM @preparedStatement;
            EXECUTE stmt;
            DEALLOCATE PREPARE stmt;

            SET @columnname = 'StartTime';
            SET @preparedStatement = (SELECT IF(
                (SELECT COUNT(*) FROM information_schema.columns
                 WHERE table_schema = @dbname AND table_name = @tablename AND column_name = @columnname) > 0,
                'ALTER TABLE staff_shifts DROP COLUMN StartTime',
                'SELECT 1'
            ));
            PREPARE stmt FROM @preparedStatement;
            EXECUTE stmt;
            DEALLOCATE PREPARE stmt;

            SET @columnname = 'EndTime';
            SET @preparedStatement = (SELECT IF(
                (SELECT COUNT(*) FROM information_schema.columns
                 WHERE table_schema = @dbname AND table_name = @tablename AND column_name = @columnname) > 0,
                'ALTER TABLE staff_shifts DROP COLUMN EndTime',
                'SELECT 1'
            ));
            PREPARE stmt FROM @preparedStatement;
            EXECUTE stmt;
            DEALLOCATE PREPARE stmt;
        ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // نسيبها فاضية - هاي Migration تصحيحية بس، ما فيها Rollback منطقي
        }
    }
}

