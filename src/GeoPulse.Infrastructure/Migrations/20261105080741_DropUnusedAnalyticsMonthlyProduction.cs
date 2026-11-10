using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoPulse.Infrastructure.Migrations
{
    /// <summary>
    /// Удаляет неиспользуемую схему analytics: помесячные агрегаты считает Aggregator в ClickHouse
    /// (gp.monthly_production), а PostgreSQL-таблица analytics.monthly_production не читалась и не писалась.
    /// </summary>
    public partial class DropUnusedAnalyticsMonthlyProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monthly_production",
                schema: "analytics");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "analytics");

            migrationBuilder.CreateTable(
                name: "monthly_production",
                schema: "analytics",
                columns: table => new
                {
                    well_id = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    days_count = table.Column<int>(type: "integer", nullable: false),
                    gas_vol_sum = table.Column<double>(type: "double precision", nullable: false),
                    oil_vol_sum = table.Column<double>(type: "double precision", nullable: false),
                    on_stream_hrs_sum = table.Column<double>(type: "double precision", nullable: false),
                    water_injection_vol_sum = table.Column<double>(type: "double precision", nullable: false),
                    water_vol_sum = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_production", x => new { x.well_id, x.year, x.month });
                });
        }
    }
}
