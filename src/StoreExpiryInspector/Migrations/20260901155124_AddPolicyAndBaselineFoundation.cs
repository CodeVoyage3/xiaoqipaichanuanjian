using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyAndBaselineFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_products_policy_code_not_blank",
                table: "products");

            migrationBuilder.AlterColumn<string>(
                name: "policy_code",
                table: "products",
                type: "TEXT",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldDefaultValue: "food_v1");

            migrationBuilder.AddColumn<string>(
                name: "expiry_management_status",
                table: "products",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "managed");

            migrationBuilder.AddColumn<int>(
                name: "policy_version",
                table: "products",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql("UPDATE products SET policy_code = 'food_expiry', policy_version = 1, expiry_management_status = 'managed' WHERE policy_code = 'food_v1';");

            migrationBuilder.CreateTable(
                name: "scope_baselines",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    scope_key = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    policy_code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    policy_version = table.Column<int>(type: "INTEGER", nullable: false),
                    created_import_id = table.Column<long>(type: "INTEGER", nullable: false),
                    business_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    is_completed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    completed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scope_baselines", x => x.id);
                    table.CheckConstraint("CK_scope_baselines_completed_fields", "(is_completed = 0 AND completed_at_utc IS NULL) OR (is_completed = 1 AND completed_at_utc IS NOT NULL)");
                    table.CheckConstraint("CK_scope_baselines_scope_key_not_blank", "length(scope_key) > 0 AND scope_key = trim(scope_key)");
                    table.CheckConstraint("CK_scope_baselines_v1_policy", "policy_code IN ('food_expiry', 'pet_expiry', 'general_long_expiry') AND policy_version = 1");
                    table.ForeignKey(
                        name: "FK_scope_baselines_imports_created_import_id",
                        column: x => x.created_import_id,
                        principalTable: "imports",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "batch_baselines",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    baseline_id = table.Column<long>(type: "INTEGER", nullable: false),
                    batch_id = table.Column<long>(type: "INTEGER", nullable: false),
                    stage_at_baseline = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    cold_start_disposition = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    catchup_window_days = table.Column<int>(type: "INTEGER", nullable: true),
                    source_task_id = table.Column<long>(type: "INTEGER", nullable: true),
                    catchup_source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_baselines", x => x.id);
                    table.CheckConstraint("CK_batch_baselines_catchup_source", "(cold_start_disposition = 'expired_catchup_task' AND length(catchup_source) > 0 AND catchup_source = trim(catchup_source)) OR (cold_start_disposition <> 'expired_catchup_task' AND catchup_source IS NULL)");
                    table.CheckConstraint("CK_batch_baselines_catchup_window", "(cold_start_disposition = 'expired_catchup_task' AND catchup_window_days BETWEEN 3 AND 30) OR (cold_start_disposition <> 'expired_catchup_task' AND catchup_window_days IS NULL)");
                    table.CheckConstraint("CK_batch_baselines_disposition", "cold_start_disposition IN ('discount50_baseline', 'discount20_baseline', 'withdraw_task', 'expired_today_task', 'expired_catchup_task', 'expired_historical_baseline', 'stock_zero_baseline')");
                    table.CheckConstraint("CK_batch_baselines_sources", "(cold_start_disposition IN ('withdraw_task', 'expired_today_task', 'expired_catchup_task') AND source_task_id IS NOT NULL) OR (cold_start_disposition NOT IN ('withdraw_task', 'expired_today_task', 'expired_catchup_task') AND source_task_id IS NULL AND catchup_source IS NULL)");
                    table.CheckConstraint("CK_batch_baselines_stage", "stage_at_baseline IN ('none', 'discount_50', 'discount_20', 'withdraw', 'expired')");
                    table.ForeignKey(
                        name: "FK_batch_baselines_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_batch_baselines_scope_baselines_baseline_id",
                        column: x => x.baseline_id,
                        principalTable: "scope_baselines",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_batch_baselines_tasks_source_task_id",
                        column: x => x.source_task_id,
                        principalTable: "tasks",
                        principalColumn: "id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_expiry_management_policy",
                table: "products",
                sql: "(expiry_management_status = 'managed' AND policy_code IN ('food_expiry', 'pet_expiry', 'general_long_expiry') AND policy_version = 1) OR (expiry_management_status IN ('excluded', 'unresolved') AND policy_code IS NULL AND policy_version IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_batch_baselines_baseline_id_batch_id",
                table: "batch_baselines",
                columns: new[] { "baseline_id", "batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_batch_baselines_batch_id",
                table: "batch_baselines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_batch_baselines_source_task_id",
                table: "batch_baselines",
                column: "source_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_scope_baselines_created_import_id",
                table: "scope_baselines",
                column: "created_import_id");

            migrationBuilder.CreateIndex(
                name: "IX_scope_baselines_scope_key_policy_code_policy_version",
                table: "scope_baselines",
                columns: new[] { "scope_key", "policy_code", "policy_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batch_baselines");

            migrationBuilder.DropTable(
                name: "scope_baselines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_expiry_management_policy",
                table: "products");

            migrationBuilder.DropColumn(
                name: "expiry_management_status",
                table: "products");

            migrationBuilder.DropColumn(
                name: "policy_version",
                table: "products");

            migrationBuilder.AlterColumn<string>(
                name: "policy_code",
                table: "products",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "food_v1",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_policy_code_not_blank",
                table: "products",
                sql: "length(trim(policy_code)) > 0");
        }
    }
}
