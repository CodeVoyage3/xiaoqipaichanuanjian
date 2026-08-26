using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class AddTasksAndDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_batches_id_product_id",
                table: "batches",
                columns: new[] { "id", "product_id" });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "open"),
                    highest_stage = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    closed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    close_reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.id);
                    table.UniqueConstraint("AK_tasks_id_product_id", x => new { x.id, x.product_id });
                    table.CheckConstraint("CK_tasks_closed_closed_at", "status = 'open' OR closed_at_utc IS NOT NULL");
                    table.CheckConstraint("CK_tasks_highest_stage", "highest_stage IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
                    table.CheckConstraint("CK_tasks_open_closed_at", "status <> 'open' OR closed_at_utc IS NULL");
                    table.CheckConstraint("CK_tasks_status", "status IN ('open', 'completed', 'system_closed')");
                    table.CheckConstraint("CK_tasks_system_closed_reason", "status <> 'system_closed' OR (close_reason IS NOT NULL AND length(trim(close_reason)) > 0)");
                    table.ForeignKey(
                        name: "FK_tasks_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "drafts",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    task_id = table.Column<long>(type: "INTEGER", nullable: false),
                    inspector_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    check_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    is_invalid = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    invalid_reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    invalidated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drafts", x => x.id);
                    table.UniqueConstraint("AK_drafts_id_task_id", x => new { x.id, x.task_id });
                    table.CheckConstraint("CK_drafts_is_invalid", "is_invalid IN (0, 1)");
                    table.CheckConstraint("CK_drafts_validity_fields", "(is_invalid = 0 AND invalid_reason IS NULL AND invalidated_at_utc IS NULL) OR (is_invalid = 1 AND invalid_reason IS NOT NULL AND length(trim(invalid_reason)) > 0 AND invalidated_at_utc IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_drafts_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "task_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    task_id = table.Column<long>(type: "INTEGER", nullable: false),
                    batch_id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    stage = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    attention_version = table.Column<int>(type: "INTEGER", nullable: false),
                    requires_reconfirmation = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_items", x => x.id);
                    table.UniqueConstraint("AK_task_items_id_task_id", x => new { x.id, x.task_id });
                    table.CheckConstraint("CK_task_items_attention_version_nonnegative", "attention_version >= 0");
                    table.CheckConstraint("CK_task_items_requires_reconfirmation", "requires_reconfirmation IN (0, 1)");
                    table.CheckConstraint("CK_task_items_stage", "stage IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
                    table.ForeignKey(
                        name: "FK_task_items_batches_batch_id_product_id",
                        columns: x => new { x.batch_id, x.product_id },
                        principalTable: "batches",
                        principalColumns: new[] { "id", "product_id" });
                    table.ForeignKey(
                        name: "FK_task_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_task_items_tasks_task_id_product_id",
                        columns: x => new { x.task_id, x.product_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "id", "product_id" });
                });

            migrationBuilder.CreateTable(
                name: "draft_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    draft_id = table.Column<long>(type: "INTEGER", nullable: false),
                    task_item_id = table.Column<long>(type: "INTEGER", nullable: false),
                    task_id = table.Column<long>(type: "INTEGER", nullable: false),
                    checked_qty = table.Column<int>(type: "INTEGER", nullable: true),
                    confirmed_attention_version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_items", x => x.id);
                    table.CheckConstraint("CK_draft_items_checked_qty_nonnegative", "checked_qty IS NULL OR checked_qty >= 0");
                    table.CheckConstraint("CK_draft_items_confirmed_attention_version_nonnegative", "confirmed_attention_version >= 0");
                    table.ForeignKey(
                        name: "FK_draft_items_drafts_draft_id_task_id",
                        columns: x => new { x.draft_id, x.task_id },
                        principalTable: "drafts",
                        principalColumns: new[] { "id", "task_id" });
                    table.ForeignKey(
                        name: "FK_draft_items_task_items_task_item_id_task_id",
                        columns: x => new { x.task_item_id, x.task_id },
                        principalTable: "task_items",
                        principalColumns: new[] { "id", "task_id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_draft_items_draft_id_task_id",
                table: "draft_items",
                columns: new[] { "draft_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "IX_draft_items_draft_id_task_item_id",
                table: "draft_items",
                columns: new[] { "draft_id", "task_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_draft_items_task_item_id_task_id",
                table: "draft_items",
                columns: new[] { "task_item_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "IX_drafts_task_id",
                table: "drafts",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_items_batch_id_product_id",
                table: "task_items",
                columns: new[] { "batch_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "IX_task_items_product_id",
                table: "task_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_task_items_task_id_batch_id",
                table: "task_items",
                columns: new[] { "task_id", "batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_items_task_id_product_id",
                table: "task_items",
                columns: new[] { "task_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_product_id_open",
                table: "tasks",
                column: "product_id",
                unique: true,
                filter: "status = 'open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draft_items");

            migrationBuilder.DropTable(
                name: "drafts");

            migrationBuilder.DropTable(
                name: "task_items");

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_batches_id_product_id",
                table: "batches");
        }
    }
}
