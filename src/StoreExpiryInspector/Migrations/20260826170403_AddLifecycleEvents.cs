using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class AddLifecycleEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lifecycle_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    batch_id = table.Column<long>(type: "INTEGER", nullable: true),
                    event_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    source_import_id = table.Column<long>(type: "INTEGER", nullable: true),
                    source_inspection_id = table.Column<long>(type: "INTEGER", nullable: true),
                    source_adjustment_id = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lifecycle_events", x => x.id);
                    table.CheckConstraint("CK_lifecycle_events_event_type", "event_type IN ('product_stock_zero', 'batch_checked_zero', 'batch_tracking_resumed', 'task_auto_closed', 'draft_invalidated')");
                    table.CheckConstraint("CK_lifecycle_events_reason_not_blank", "length(reason) > 0 AND reason = trim(reason)");
                    table.CheckConstraint("CK_lifecycle_events_single_source", "(source_import_id IS NOT NULL) + (source_inspection_id IS NOT NULL) + (source_adjustment_id IS NOT NULL) <= 1");
                    table.ForeignKey(
                        name: "FK_lifecycle_events_batches_batch_id_product_id",
                        columns: x => new { x.batch_id, x.product_id },
                        principalTable: "batches",
                        principalColumns: new[] { "id", "product_id" });
                    table.ForeignKey(
                        name: "FK_lifecycle_events_imports_source_import_id",
                        column: x => x.source_import_id,
                        principalTable: "imports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_lifecycle_events_inspections_source_inspection_id_product_id",
                        columns: x => new { x.source_inspection_id, x.product_id },
                        principalTable: "inspections",
                        principalColumns: new[] { "id", "product_id" });
                    table.ForeignKey(
                        name: "FK_lifecycle_events_inventory_adjustments_source_adjustment_id",
                        column: x => x.source_adjustment_id,
                        principalTable: "inventory_adjustments",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_lifecycle_events_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_events_batch_id_product_id_occurred_at_utc_id",
                table: "lifecycle_events",
                columns: new[] { "batch_id", "product_id", "occurred_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_events_product_id_occurred_at_utc_id",
                table: "lifecycle_events",
                columns: new[] { "product_id", "occurred_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_events_source_adjustment_id",
                table: "lifecycle_events",
                column: "source_adjustment_id");

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_events_source_import_id",
                table: "lifecycle_events",
                column: "source_import_id");

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_events_source_inspection_id_product_id",
                table: "lifecycle_events",
                columns: new[] { "source_inspection_id", "product_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lifecycle_events");
        }
    }
}
