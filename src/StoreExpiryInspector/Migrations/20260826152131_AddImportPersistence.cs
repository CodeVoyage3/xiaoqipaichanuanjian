using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class AddImportPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "imports",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    source_file_name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    source_file_sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    parsed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    confirmed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    product_count = table.Column<int>(type: "INTEGER", nullable: false),
                    batch_count = table.Column<int>(type: "INTEGER", nullable: false),
                    new_product_count = table.Column<int>(type: "INTEGER", nullable: false),
                    new_batch_count = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_batch_count = table.Column<int>(type: "INTEGER", nullable: false),
                    issue_count = table.Column<int>(type: "INTEGER", nullable: false),
                    unsupported_category_count = table.Column<int>(type: "INTEGER", nullable: false),
                    new_task_product_count = table.Column<int>(type: "INTEGER", nullable: false),
                    pre_import_snapshot_path = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    is_undone = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    undone_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imports", x => x.id);
                    table.CheckConstraint("CK_imports_batch_count_nonnegative", "batch_count >= 0");
                    table.CheckConstraint("CK_imports_issue_count_nonnegative", "issue_count >= 0");
                    table.CheckConstraint("CK_imports_new_batch_count_nonnegative", "new_batch_count >= 0");
                    table.CheckConstraint("CK_imports_new_product_count_nonnegative", "new_product_count >= 0");
                    table.CheckConstraint("CK_imports_new_task_product_count_nonnegative", "new_task_product_count >= 0");
                    table.CheckConstraint("CK_imports_product_count_nonnegative", "product_count >= 0");
                    table.CheckConstraint("CK_imports_source_file_name_not_blank", "length(source_file_name) > 0 AND source_file_name = trim(source_file_name)");
                    table.CheckConstraint("CK_imports_source_file_sha256_lower_hex", "length(source_file_sha256) = 64 AND source_file_sha256 NOT GLOB '*[^0-9a-f]*'");
                    table.CheckConstraint("CK_imports_status_not_blank", "length(status) > 0 AND status = trim(status)");
                    table.CheckConstraint("CK_imports_undone_fields", "(is_undone = 0 AND undone_at_utc IS NULL) OR (is_undone = 1 AND undone_at_utc IS NOT NULL)");
                    table.CheckConstraint("CK_imports_unsupported_category_count_nonnegative", "unsupported_category_count >= 0");
                    table.CheckConstraint("CK_imports_updated_batch_count_nonnegative", "updated_batch_count >= 0");
                });

            migrationBuilder.CreateTable(
                name: "import_issues",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    import_id = table.Column<long>(type: "INTEGER", nullable: false),
                    row_number = table.Column<int>(type: "INTEGER", nullable: true),
                    issue_type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    field_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    safe_summary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_issues", x => x.id);
                    table.CheckConstraint("CK_import_issues_issue_type_not_blank", "length(issue_type) > 0 AND issue_type = trim(issue_type)");
                    table.CheckConstraint("CK_import_issues_row_number_positive", "row_number IS NULL OR row_number > 0");
                    table.CheckConstraint("CK_import_issues_safe_summary_not_blank", "length(safe_summary) > 0 AND safe_summary = trim(safe_summary)");
                    table.ForeignKey(
                        name: "FK_import_issues_imports_import_id",
                        column: x => x.import_id,
                        principalTable: "imports",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "import_workbooks",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    import_id = table.Column<long>(type: "INTEGER", nullable: false),
                    original_file_name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    saved_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_workbooks", x => x.id);
                    table.CheckConstraint("CK_import_workbooks_content_not_empty", "length(content) > 0");
                    table.CheckConstraint("CK_import_workbooks_original_file_name_not_blank", "length(original_file_name) > 0 AND original_file_name = trim(original_file_name)");
                    table.CheckConstraint("CK_import_workbooks_sha256_lower_hex", "length(sha256) = 64 AND sha256 NOT GLOB '*[^0-9a-f]*'");
                    table.ForeignKey(
                        name: "FK_import_workbooks_imports_import_id",
                        column: x => x.import_id,
                        principalTable: "imports",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_last_seen_import_id",
                table: "products",
                column: "last_seen_import_id");

            migrationBuilder.CreateIndex(
                name: "IX_batches_last_seen_import_id",
                table: "batches",
                column: "last_seen_import_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_issues_import_id_row_number_id",
                table: "import_issues",
                columns: new[] { "import_id", "row_number", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_import_workbooks_import_id",
                table: "import_workbooks",
                column: "import_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_imports_source_file_sha256",
                table: "imports",
                column: "source_file_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_imports_status_confirmed_at_utc_id",
                table: "imports",
                columns: new[] { "status", "confirmed_at_utc", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_batches_imports_last_seen_import_id",
                table: "batches",
                column: "last_seen_import_id",
                principalTable: "imports",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_products_imports_last_seen_import_id",
                table: "products",
                column: "last_seen_import_id",
                principalTable: "imports",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_batches_imports_last_seen_import_id",
                table: "batches");

            migrationBuilder.DropForeignKey(
                name: "FK_products_imports_last_seen_import_id",
                table: "products");

            migrationBuilder.DropTable(
                name: "import_issues");

            migrationBuilder.DropTable(
                name: "import_workbooks");

            migrationBuilder.DropTable(
                name: "imports");

            migrationBuilder.DropIndex(
                name: "IX_products_last_seen_import_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_batches_last_seen_import_id",
                table: "batches");
        }
    }
}
