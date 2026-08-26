using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_adjustments",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    excel_stock_qty_snapshot = table.Column<int>(type: "INTEGER", nullable: false),
                    adjusted_stock_qty = table.Column<int>(type: "INTEGER", nullable: false),
                    adjusted_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_adjustments", x => x.id);
                    table.CheckConstraint("CK_inventory_adjustments_adjusted_stock_qty_nonnegative", "adjusted_stock_qty >= 0");
                    table.CheckConstraint("CK_inventory_adjustments_excel_stock_qty_snapshot_nonnegative", "excel_stock_qty_snapshot >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_adjustments_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_product_id_adjusted_at_utc_id",
                table: "inventory_adjustments",
                columns: new[] { "product_id", "adjusted_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_adjustments");
        }
    }
}
