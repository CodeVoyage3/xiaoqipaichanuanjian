using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "backups",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    backup_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    file_path = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    verification_status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backups", x => x.id);
                    table.CheckConstraint("CK_backups_backup_type", "backup_type IN ('auto', 'manual', 'pre_import', 'pre_restore', 'pre_upgrade')");
                    table.CheckConstraint("CK_backups_file_path_not_blank", "length(file_path) > 0 AND file_path = trim(file_path)");
                    table.CheckConstraint("CK_backups_sha256_lower_hex", "length(sha256) = 64 AND sha256 NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_backups_verification_status_not_blank", "length(verification_status) > 0 AND verification_status = trim(verification_status)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_backups_backup_type_created_at_utc_id",
                table: "backups",
                columns: new[] { "backup_type", "created_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backups");
        }
    }
}
