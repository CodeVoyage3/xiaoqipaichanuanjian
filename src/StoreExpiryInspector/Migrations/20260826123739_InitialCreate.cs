using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    product_code = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    current_name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    current_barcode = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    category_code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "food"),
                    policy_code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "food_v1"),
                    excel_stock_qty = table.Column<int>(type: "INTEGER", nullable: false),
                    effective_stock_qty = table.Column<int>(type: "INTEGER", nullable: false),
                    effective_stock_source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    lifecycle_generation = table.Column<int>(type: "INTEGER", nullable: false),
                    is_stock_zero_terminated = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    last_seen_import_id = table.Column<long>(type: "INTEGER", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.CheckConstraint("CK_products_category_code_not_blank", "length(trim(category_code)) > 0");
                    table.CheckConstraint("CK_products_effective_stock_qty_nonnegative", "effective_stock_qty >= 0");
                    table.CheckConstraint("CK_products_excel_stock_qty_nonnegative", "excel_stock_qty >= 0");
                    table.CheckConstraint("CK_products_policy_code_not_blank", "length(trim(policy_code)) > 0");
                    table.CheckConstraint("CK_products_product_code_not_blank", "length(product_code) > 0 AND product_code = trim(product_code)");
                });

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    production_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    shelf_life_value = table.Column<int>(type: "INTEGER", nullable: false),
                    shelf_life_unit = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false, defaultValue: "D"),
                    current_arrival_qty = table.Column<int>(type: "INTEGER", nullable: false),
                    max_arrival_qty = table.Column<int>(type: "INTEGER", nullable: false),
                    source_discount_reference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    lifecycle_generation = table.Column<int>(type: "INTEGER", nullable: false),
                    tracking_status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "active"),
                    stop_reason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    stopped_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    current_stage = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "none"),
                    next_trigger_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    attention_version = table.Column<int>(type: "INTEGER", nullable: false),
                    handled_attention_version = table.Column<int>(type: "INTEGER", nullable: false),
                    last_seen_import_id = table.Column<long>(type: "INTEGER", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batches", x => x.id);
                    table.CheckConstraint("CK_batches_current_arrival_qty_nonnegative", "current_arrival_qty >= 0");
                    table.CheckConstraint("CK_batches_max_arrival_qty_nonnegative", "max_arrival_qty >= 0");
                    table.CheckConstraint("CK_batches_shelf_life_unit", "shelf_life_unit IN ('M', 'D', 'Y')");
                    table.ForeignKey(
                        name: "FK_batches_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_batches_expiry_date",
                table: "batches",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "IX_batches_product_id",
                table: "batches",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_batches_product_id_expiry_date",
                table: "batches",
                columns: new[] { "product_id", "expiry_date" },
                unique: true,
                filter: "production_date IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_batches_product_id_production_date_expiry_date",
                table: "batches",
                columns: new[] { "product_id", "production_date", "expiry_date" },
                unique: true,
                filter: "production_date IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_batches_tracking_status_next_trigger_date",
                table: "batches",
                columns: new[] { "tracking_status", "next_trigger_date" });

            migrationBuilder.CreateIndex(
                name: "IX_products_product_code",
                table: "products",
                column: "product_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}
