using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inspections",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    task_id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_code_snapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    product_name_snapshot = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    barcode_snapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    stage_snapshot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    stock_qty_snapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    inspector_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    check_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspections", x => x.id);
                    table.UniqueConstraint("AK_inspections_id_product_id", x => new { x.id, x.product_id });
                    table.CheckConstraint("CK_inspections_inspector_name_not_blank", "length(inspector_name) > 0 AND inspector_name = trim(inspector_name)");
                    table.CheckConstraint("CK_inspections_product_code_snapshot_not_blank", "length(product_code_snapshot) > 0 AND product_code_snapshot = trim(product_code_snapshot)");
                    table.CheckConstraint("CK_inspections_stage_snapshot", "stage_snapshot IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
                    table.CheckConstraint("CK_inspections_stock_qty_snapshot_nonnegative", "stock_qty_snapshot >= 0");
                    table.ForeignKey(
                        name: "FK_inspections_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_inspections_tasks_task_id_product_id",
                        columns: x => new { x.task_id, x.product_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "id", "product_id" });
                });

            migrationBuilder.CreateTable(
                name: "inspection_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    inspection_id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    batch_id = table.Column<long>(type: "INTEGER", nullable: false),
                    production_date_snapshot = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    expiry_date_snapshot = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    stage_snapshot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    arrival_qty_snapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    checked_qty = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_items", x => x.id);
                    table.CheckConstraint("CK_inspection_items_arrival_qty_snapshot_nonnegative", "arrival_qty_snapshot >= 0");
                    table.CheckConstraint("CK_inspection_items_checked_qty_nonnegative", "checked_qty >= 0");
                    table.CheckConstraint("CK_inspection_items_stage_snapshot", "stage_snapshot IN ('discount_50', 'discount_20', 'withdraw', 'expired')");
                    table.ForeignKey(
                        name: "FK_inspection_items_batches_batch_id_product_id",
                        columns: x => new { x.batch_id, x.product_id },
                        principalTable: "batches",
                        principalColumns: new[] { "id", "product_id" });
                    table.ForeignKey(
                        name: "FK_inspection_items_inspections_inspection_id_product_id",
                        columns: x => new { x.inspection_id, x.product_id },
                        principalTable: "inspections",
                        principalColumns: new[] { "id", "product_id" });
                    table.ForeignKey(
                        name: "FK_inspection_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "inspection_item_revisions",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    inspection_item_id = table.Column<long>(type: "INTEGER", nullable: false),
                    previous_checked_qty = table.Column<int>(type: "INTEGER", nullable: false),
                    new_checked_qty = table.Column<int>(type: "INTEGER", nullable: false),
                    changed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_item_revisions", x => x.id);
                    table.CheckConstraint("CK_inspection_item_revisions_checked_qty_changed", "previous_checked_qty <> new_checked_qty");
                    table.CheckConstraint("CK_inspection_item_revisions_new_checked_qty_nonnegative", "new_checked_qty >= 0");
                    table.CheckConstraint("CK_inspection_item_revisions_previous_checked_qty_nonnegative", "previous_checked_qty >= 0");
                    table.ForeignKey(
                        name: "FK_inspection_item_revisions_inspection_items_inspection_item_id",
                        column: x => x.inspection_item_id,
                        principalTable: "inspection_items",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_item_revisions_inspection_item_id_changed_at_utc_id",
                table: "inspection_item_revisions",
                columns: new[] { "inspection_item_id", "changed_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_batch_id_product_id",
                table: "inspection_items",
                columns: new[] { "batch_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_inspection_id_batch_id",
                table: "inspection_items",
                columns: new[] { "inspection_id", "batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_inspection_id_product_id",
                table: "inspection_items",
                columns: new[] { "inspection_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_product_id",
                table: "inspection_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_product_id",
                table: "inspections",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_task_id",
                table: "inspections",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inspections_task_id_product_id",
                table: "inspections",
                columns: new[] { "task_id", "product_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspection_item_revisions");

            migrationBuilder.DropTable(
                name: "inspection_items");

            migrationBuilder.DropTable(
                name: "inspections");
        }
    }
}
