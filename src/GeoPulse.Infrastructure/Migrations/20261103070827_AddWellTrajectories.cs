using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoPulse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWellTrajectories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "well_trajectories",
                columns: table => new
                {
                    well_id = table.Column<int>(type: "integer", nullable: false),
                    md = table.Column<double>(type: "double precision", nullable: false),
                    tvd = table.Column<double>(type: "double precision", nullable: false),
                    inclination = table.Column<double>(type: "double precision", nullable: false),
                    azimuth = table.Column<double>(type: "double precision", nullable: false),
                    easting = table.Column<double>(type: "double precision", nullable: false),
                    northing = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_well_trajectories", x => new { x.well_id, x.md });
                    table.ForeignKey(
                        name: "fk_well_trajectories_wells",
                        column: x => x.well_id,
                        principalTable: "wells",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "well_trajectory_meta",
                columns: table => new
                {
                    well_id = table.Column<int>(type: "integer", nullable: false),
                    td_md = table.Column<double>(type: "double precision", nullable: true),
                    td_tvd = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_well_trajectory_meta", x => x.well_id);
                    table.ForeignKey(
                        name: "fk_well_trajectory_meta_wells",
                        column: x => x.well_id,
                        principalTable: "wells",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "well_trajectories");

            migrationBuilder.DropTable(
                name: "well_trajectory_meta");
        }
    }
}
