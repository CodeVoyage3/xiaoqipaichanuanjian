using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsAndAppState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_state",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    last_reminder_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    last_normal_run_date = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_state", x => x.id);
                    table.CheckConstraint("CK_app_state_id_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    reminder_minute_of_day = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 600),
                    auto_start_enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.id);
                    table.CheckConstraint("CK_settings_auto_start_enabled", "auto_start_enabled IN (0, 1)");
                    table.CheckConstraint("CK_settings_id_singleton", "id = 1");
                    table.CheckConstraint("CK_settings_reminder_minute_of_day_range", "reminder_minute_of_day BETWEEN 0 AND 1439");
                });

            migrationBuilder.InsertData(
                table: "app_state",
                columns: new[] { "id" },
                values: new object[] { 1L });

            migrationBuilder.InsertData(
                table: "settings",
                columns: new[] { "id", "reminder_minute_of_day", "auto_start_enabled" },
                values: new object[] { 1L, 600, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_state");

            migrationBuilder.DropTable(
                name: "settings");
        }
    }
}
