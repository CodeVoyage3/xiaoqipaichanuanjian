using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreExpiryInspector.Migrations
{
    /// <inheritdoc />
    public partial class AdjustCatchupWindowConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("CK_batch_baselines_catchup_window", "batch_baselines");
            migrationBuilder.AddCheckConstraint("CK_batch_baselines_catchup_window", "batch_baselines", "(cold_start_disposition = 'expired_catchup_task' AND catchup_window_days BETWEEN 1 AND 30) OR (cold_start_disposition <> 'expired_catchup_task' AND catchup_window_days IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("CK_batch_baselines_catchup_window", "batch_baselines");
            migrationBuilder.AddCheckConstraint("CK_batch_baselines_catchup_window", "batch_baselines", "(cold_start_disposition = 'expired_catchup_task' AND catchup_window_days BETWEEN 3 AND 30) OR (cold_start_disposition <> 'expired_catchup_task' AND catchup_window_days IS NULL)");
        }
    }
}
