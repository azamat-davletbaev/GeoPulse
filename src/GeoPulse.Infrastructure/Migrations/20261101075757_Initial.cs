using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoPulse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wells",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    npd_well_bore_code = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wells", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "daily_production",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    well_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    on_stream_hrs = table.Column<double>(type: "double precision", nullable: true),
                    avg_downhole_pressure = table.Column<double>(type: "double precision", nullable: true),
                    avg_downhole_temperature = table.Column<double>(type: "double precision", nullable: true),
                    avg_dp_tubing = table.Column<double>(type: "double precision", nullable: true),
                    avg_annulus_pressure = table.Column<double>(type: "double precision", nullable: true),
                    avg_choke_size = table.Column<double>(type: "double precision", nullable: true),
                    avg_whp = table.Column<double>(type: "double precision", nullable: true),
                    avg_wht = table.Column<double>(type: "double precision", nullable: true),
                    dp_choke_size = table.Column<double>(type: "double precision", nullable: true),
                    oil_vol = table.Column<double>(type: "double precision", nullable: true),
                    gas_vol = table.Column<double>(type: "double precision", nullable: true),
                    water_vol = table.Column<double>(type: "double precision", nullable: true),
                    water_injection_vol = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_production", x => x.id);
                    table.ForeignKey(
                        name: "fk_daily_production_wells",
                        column: x => x.well_id,
                        principalTable: "wells",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_production_well_id_date",
                table: "daily_production",
                columns: new[] { "well_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wells_npd_well_bore_code",
                table: "wells",
                column: "npd_well_bore_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_production");

            migrationBuilder.DropTable(
                name: "wells");
        }
    }
}
