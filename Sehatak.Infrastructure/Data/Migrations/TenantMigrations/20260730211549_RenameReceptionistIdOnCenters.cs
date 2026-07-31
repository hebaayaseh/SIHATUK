using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sehatak.Infrastructure.Data.Migrations.TenantMigrations
{
    /// <inheritdoc />
    public partial class RenameReceptionistIdOnCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
        SET @col_exists = (
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'payments'
              AND COLUMN_NAME = 'ReceptionistId'
        );
        SET @sql = IF(@col_exists > 0,
            'ALTER TABLE `payments` CHANGE COLUMN `ReceptionistId` `RecordedByStaffId` int(11) NULL;',
            'SELECT 1;'
        );
        PREPARE stmt FROM @sql;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
        SET @col_exists = (
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'payments'
              AND COLUMN_NAME = 'RecordedByStaffId'
        );
        SET @sql = IF(@col_exists > 0,
            'ALTER TABLE `payments` CHANGE COLUMN `RecordedByStaffId` `ReceptionistId` int(11) NULL;',
            'SELECT 1;'
        );
        PREPARE stmt FROM @sql;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    ");
        }
    }
}
