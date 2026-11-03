using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoPulse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWellLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "well_locations",
                columns: table => new
                {
                    well_id = table.Column<int>(type: "integer", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_well_locations", x => x.well_id);
                    table.ForeignKey(
                        name: "fk_well_locations_wells",
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
                name: "well_locations");
        }
    }
}
