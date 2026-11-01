using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoPulse.WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    source_root = table.Column<string>(type: "text", nullable: true),
                    batch_size = table.Column<int>(type: "integer", nullable: false),
                    wells = table.Column<int>(type: "integer", nullable: false),
                    wellbores = table.Column<int>(type: "integer", nullable: false),
                    drilling_reports = table.Column<int>(type: "integer", nullable: false),
                    activities = table.Column<int>(type: "integer", nullable: false),
                    fluids = table.Column<int>(type: "integer", nullable: false),
                    report_survey_stations = table.Column<int>(type: "integer", nullable: false),
                    survey_reports = table.Column<int>(type: "integer", nullable: false),
                    survey_stations = table.Column<int>(type: "integer", nullable: false),
                    production_daily = table.Column<int>(type: "integer", nullable: false),
                    production_monthly = table.Column<int>(type: "integer", nullable: false),
                    total_rows = table.Column<long>(type: "bigint", nullable: false),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wells",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    @operator = table.Column<string>(name: "operator", type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wells", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wellbores",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    well_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    npd_wellbore_code = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wellbores", x => x.id);
                    table.ForeignKey(
                        name: "fk_wellbores_wells",
                        column: x => x.well_id,
                        principalTable: "wells",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_reports",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wellbore_id = table.Column<int>(type: "integer", nullable: false),
                    source_file = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    dtim_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dtim_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version_kind = table.Column<string>(type: "text", nullable: true),
                    create_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    report_no = table.Column<int>(type: "integer", nullable: true),
                    md = table.Column<double>(type: "double precision", nullable: true),
                    tvd = table.Column<double>(type: "double precision", nullable: true),
                    dia_hole = table.Column<double>(type: "double precision", nullable: true),
                    dia_pilot = table.Column<double>(type: "double precision", nullable: true),
                    dtim_dia_hole_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    md_dia_hole_start = table.Column<double>(type: "double precision", nullable: true),
                    strength_form = table.Column<double>(type: "double precision", nullable: true),
                    md_strength_form = table.Column<double>(type: "double precision", nullable: true),
                    tvd_strength_form = table.Column<double>(type: "double precision", nullable: true),
                    md_csg_last = table.Column<double>(type: "double precision", nullable: true),
                    tvd_csg_last = table.Column<double>(type: "double precision", nullable: true),
                    pres_test_type = table.Column<string>(type: "text", nullable: true),
                    md_planned = table.Column<double>(type: "double precision", nullable: true),
                    dist_drill = table.Column<double>(type: "double precision", nullable: true),
                    elev_kelly = table.Column<double>(type: "double precision", nullable: true),
                    wellhead_elevation = table.Column<double>(type: "double precision", nullable: true),
                    water_depth = table.Column<double>(type: "double precision", nullable: true),
                    sum24_hr = table.Column<string>(type: "text", nullable: true),
                    forecast24_hr = table.Column<string>(type: "text", nullable: true),
                    rop_current = table.Column<double>(type: "double precision", nullable: true),
                    tight_well = table.Column<bool>(type: "boolean", nullable: true),
                    hpht = table.Column<bool>(type: "boolean", nullable: true),
                    fixed_rig = table.Column<bool>(type: "boolean", nullable: true),
                    md_kickoff = table.Column<double>(type: "double precision", nullable: true),
                    md_plug_top = table.Column<double>(type: "double precision", nullable: true),
                    avg_pres_bh = table.Column<double>(type: "double precision", nullable: true),
                    avg_temp_bh = table.Column<double>(type: "double precision", nullable: true),
                    dtim_spud = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    date_drill_complete = table.Column<DateOnly>(type: "date", nullable: true),
                    days_ahead = table.Column<double>(type: "double precision", nullable: true),
                    days_behind = table.Column<double>(type: "double precision", nullable: true),
                    @operator = table.Column<string>(name: "operator", type: "text", nullable: true),
                    drill_contractor = table.Column<string>(type: "text", nullable: true),
                    rig_name = table.Column<string>(type: "text", nullable: true),
                    rig_code = table.Column<string>(type: "text", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_drilling_reports_wellbores",
                        column: x => x.wellbore_id,
                        principalTable: "wellbores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_daily",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wellbore_id = table.Column<int>(type: "integer", nullable: false),
                    production_date = table.Column<DateOnly>(type: "date", nullable: false),
                    wellbore_code = table.Column<string>(type: "text", nullable: true),
                    npd_wellbore_code = table.Column<int>(type: "integer", nullable: true),
                    npd_wellbore_name = table.Column<string>(type: "text", nullable: true),
                    npd_field_code = table.Column<int>(type: "integer", nullable: true),
                    npd_field_name = table.Column<string>(type: "text", nullable: true),
                    npd_facility_code = table.Column<int>(type: "integer", nullable: true),
                    npd_facility_name = table.Column<string>(type: "text", nullable: true),
                    on_stream_hrs = table.Column<double>(type: "double precision", nullable: true),
                    avg_downhole_pressure = table.Column<double>(type: "double precision", nullable: true),
                    avg_downhole_temperature = table.Column<double>(type: "double precision", nullable: true),
                    avg_dp_tubing = table.Column<double>(type: "double precision", nullable: true),
                    avg_annulus_pressure = table.Column<double>(type: "double precision", nullable: true),
                    avg_choke_size_p = table.Column<double>(type: "double precision", nullable: true),
                    avg_choke_uom = table.Column<string>(type: "text", nullable: true),
                    avg_whp = table.Column<double>(type: "double precision", nullable: true),
                    avg_wht = table.Column<double>(type: "double precision", nullable: true),
                    dp_choke_size = table.Column<double>(type: "double precision", nullable: true),
                    bore_oil_vol = table.Column<double>(type: "double precision", nullable: true),
                    bore_gas_vol = table.Column<double>(type: "double precision", nullable: true),
                    bore_wat_vol = table.Column<double>(type: "double precision", nullable: true),
                    bore_wi_vol = table.Column<double>(type: "double precision", nullable: true),
                    flow_kind = table.Column<string>(type: "text", nullable: true),
                    well_type = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_daily", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_daily_wellbores",
                        column: x => x.wellbore_id,
                        principalTable: "wellbores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "production_monthly",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wellbore_id = table.Column<int>(type: "integer", nullable: false),
                    wellbore_name = table.Column<string>(type: "text", nullable: true),
                    npd_wellbore_code = table.Column<int>(type: "integer", nullable: true),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    on_stream_hrs = table.Column<double>(type: "double precision", nullable: true),
                    oil_vol = table.Column<double>(type: "double precision", nullable: true),
                    gas_vol = table.Column<double>(type: "double precision", nullable: true),
                    water_vol = table.Column<double>(type: "double precision", nullable: true),
                    gas_injection_vol = table.Column<double>(type: "double precision", nullable: true),
                    water_injection_vol = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_monthly", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_monthly_wellbores",
                        column: x => x.wellbore_id,
                        principalTable: "wellbores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wellbore_survey_reports",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wellbore_id = table.Column<int>(type: "integer", nullable: false),
                    source_file = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    report_kind = table.Column<string>(type: "text", nullable: true),
                    survey_name = table.Column<string>(type: "text", nullable: true),
                    company = table.Column<string>(type: "text", nullable: true),
                    field_name = table.Column<string>(type: "text", nullable: true),
                    well_name_raw = table.Column<string>(type: "text", nullable: true),
                    wellbore_name_raw = table.Column<string>(type: "text", nullable: true),
                    drilled_from = table.Column<string>(type: "text", nullable: true),
                    calculation_method = table.Column<string>(type: "text", nullable: true),
                    datum_name = table.Column<string>(type: "text", nullable: true),
                    kick_off_md = table.Column<double>(type: "double precision", nullable: true),
                    survey_date = table.Column<DateOnly>(type: "date", nullable: true),
                    extraction_date = table.Column<DateOnly>(type: "date", nullable: true),
                    surface_ew = table.Column<double>(type: "double precision", nullable: true),
                    surface_ns = table.Column<double>(type: "double precision", nullable: true),
                    surface_latitude = table.Column<double>(type: "double precision", nullable: true),
                    surface_longitude = table.Column<double>(type: "double precision", nullable: true),
                    bottom_hole_md = table.Column<double>(type: "double precision", nullable: true),
                    bottom_hole_tvd = table.Column<double>(type: "double precision", nullable: true),
                    bottom_hole_ew = table.Column<double>(type: "double precision", nullable: true),
                    bottom_hole_ns = table.Column<double>(type: "double precision", nullable: true),
                    water_depth = table.Column<double>(type: "double precision", nullable: true),
                    kb_to_wh = table.Column<double>(type: "double precision", nullable: true),
                    kb_to_msl = table.Column<double>(type: "double precision", nullable: true),
                    system_datum = table.Column<string>(type: "text", nullable: true),
                    map_zone = table.Column<string>(type: "text", nullable: true),
                    north_reference = table.Column<string>(type: "text", nullable: true),
                    geo_datum = table.Column<string>(type: "text", nullable: true),
                    vertical_section_direction = table.Column<double>(type: "double precision", nullable: true),
                    station_count = table.Column<int>(type: "integer", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wellbore_survey_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_wellbore_survey_reports_wellbores",
                        column: x => x.wellbore_id,
                        principalTable: "wellbores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_activities",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    dtim_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dtim_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    md = table.Column<double>(type: "double precision", nullable: true),
                    phase = table.Column<string>(type: "text", nullable: true),
                    proprietary_code = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: true),
                    state_detail_activity = table.Column<string>(type: "text", nullable: true),
                    comments = table.Column<string>(type: "text", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_activities", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_activities_drilling_reports_drilling_report~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_activities_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_bit_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    code_mfg = table.Column<string>(type: "text", nullable: true),
                    manufacturer = table.Column<string>(type: "text", nullable: true),
                    dia_bit = table.Column<double>(type: "double precision", nullable: true),
                    code_iadc = table.Column<string>(type: "text", nullable: true),
                    num_bit = table.Column<string>(type: "text", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    cond_final_dull = table.Column<string>(type: "text", nullable: true),
                    cond_final_bearing = table.Column<string>(type: "text", nullable: true),
                    cond_final_gauge = table.Column<string>(type: "text", nullable: true),
                    cond_final_inner = table.Column<string>(type: "text", nullable: true),
                    cond_final_outer = table.Column<string>(type: "text", nullable: true),
                    cond_final_location = table.Column<string>(type: "text", nullable: true),
                    cond_final_reason = table.Column<string>(type: "text", nullable: true),
                    cond_final_other = table.Column<string>(type: "text", nullable: true),
                    e_tim_op_bit = table.Column<double>(type: "double precision", nullable: true),
                    hrs_drilled = table.Column<double>(type: "double precision", nullable: true),
                    md_hole_start = table.Column<double>(type: "double precision", nullable: true),
                    md_hole_stop = table.Column<double>(type: "double precision", nullable: true),
                    md_tot_hole_made = table.Column<double>(type: "double precision", nullable: true),
                    rop_av = table.Column<double>(type: "double precision", nullable: true),
                    tot_hrs_drilled = table.Column<double>(type: "double precision", nullable: true),
                    tot_rop = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_bit_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_bit_records_drilling_reports_drilling_repor~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_bit_records_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_casing_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: true),
                    grade = table.Column<string>(type: "text", nullable: true),
                    connection = table.Column<string>(type: "text", nullable: true),
                    length = table.Column<double>(type: "double precision", nullable: true),
                    md_top = table.Column<double>(type: "double precision", nullable: true),
                    md_bottom = table.Column<double>(type: "double precision", nullable: true),
                    od = table.Column<double>(type: "double precision", nullable: true),
                    inner_diameter = table.Column<double>(type: "double precision", nullable: true),
                    weight = table.Column<double>(type: "double precision", nullable: true),
                    run_casing_type = table.Column<string>(type: "text", nullable: true),
                    run_description = table.Column<string>(type: "text", nullable: true),
                    run_dtim_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_dtim_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_casing_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_casing_runs_drilling_reports_drilling_repor~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_casing_runs_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_core_infos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    core_number = table.Column<string>(type: "text", nullable: true),
                    core_description = table.Column<string>(type: "text", nullable: true),
                    inner_barrel_type = table.Column<string>(type: "text", nullable: true),
                    len_barrel = table.Column<double>(type: "double precision", nullable: true),
                    len_recovered = table.Column<double>(type: "double precision", nullable: true),
                    recover_pct = table.Column<double>(type: "double precision", nullable: true),
                    md_top = table.Column<double>(type: "double precision", nullable: true),
                    md_bottom = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_core_infos", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_core_infos_drilling_reports_drilling_report~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_core_infos_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_equip_failures",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dtim_repair = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    equip_class = table.Column<string>(type: "text", nullable: true),
                    e_tim_miss_production = table.Column<double>(type: "double precision", nullable: true),
                    md = table.Column<double>(type: "double precision", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_equip_failures", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_equip_failures_drilling_reports_drilling_re~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_equip_failures_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_fluids",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    fluid_type = table.Column<string>(type: "text", nullable: true),
                    location_sample = table.Column<string>(type: "text", nullable: true),
                    dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    md = table.Column<double>(type: "double precision", nullable: true),
                    md_datum = table.Column<string>(type: "text", nullable: true),
                    pres_bop_rating = table.Column<double>(type: "double precision", nullable: true),
                    mud_class = table.Column<string>(type: "text", nullable: true),
                    density = table.Column<double>(type: "double precision", nullable: true),
                    vis_funnel = table.Column<double>(type: "double precision", nullable: true),
                    pv = table.Column<double>(type: "double precision", nullable: true),
                    yp = table.Column<double>(type: "double precision", nullable: true),
                    temp_hthp = table.Column<double>(type: "double precision", nullable: true),
                    filtrate_ltlp = table.Column<double>(type: "double precision", nullable: true),
                    filter_cake_ltlp = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_fluids", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_fluids_drilling_reports_drilling_report_id1",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_fluids_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_gas_readings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reading_type = table.Column<string>(type: "text", nullable: true),
                    md_top = table.Column<double>(type: "double precision", nullable: true),
                    tvd_top = table.Column<double>(type: "double precision", nullable: true),
                    gas_high = table.Column<double>(type: "double precision", nullable: true),
                    methane = table.Column<double>(type: "double precision", nullable: true),
                    ethane = table.Column<double>(type: "double precision", nullable: true),
                    propane = table.Column<double>(type: "double precision", nullable: true),
                    i_butane = table.Column<double>(type: "double precision", nullable: true),
                    n_butane = table.Column<double>(type: "double precision", nullable: true),
                    i_pentane = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_gas_readings", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_gas_readings_drilling_reports_drilling_repo~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_gas_readings_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_lith_shows",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lithology = table.Column<string>(type: "text", nullable: true),
                    show = table.Column<string>(type: "text", nullable: true),
                    md_top = table.Column<double>(type: "double precision", nullable: true),
                    md_bottom = table.Column<double>(type: "double precision", nullable: true),
                    tvd_top = table.Column<double>(type: "double precision", nullable: true),
                    tvd_bottom = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_lith_shows", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_lith_shows_drilling_reports_drilling_report~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_lith_shows_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_log_infos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    run_number = table.Column<int>(type: "integer", nullable: true),
                    service_company = table.Column<string>(type: "text", nullable: true),
                    md_top = table.Column<double>(type: "double precision", nullable: true),
                    md_bottom = table.Column<double>(type: "double precision", nullable: true),
                    tool = table.Column<string>(type: "text", nullable: true),
                    temp_bhst = table.Column<double>(type: "double precision", nullable: true),
                    e_tim_static = table.Column<double>(type: "double precision", nullable: true),
                    md_temp_tool = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_log_infos", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_log_infos_drilling_reports_drilling_report_~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_log_infos_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_perf_infos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    dtim_open = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dtim_close = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    md_top = table.Column<double>(type: "double precision", nullable: true),
                    md_bottom = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_perf_infos", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_perf_infos_drilling_reports_drilling_report~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_perf_infos_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_pore_pressures",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    md = table.Column<double>(type: "double precision", nullable: true),
                    equivalent_mud_weight = table.Column<double>(type: "double precision", nullable: true),
                    reading_kind = table.Column<string>(type: "text", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_pore_pressures", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_pore_pressures_drilling_reports_drilling_re~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_pore_pressures_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_strat_infos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    md_top = table.Column<double>(type: "double precision", nullable: true),
                    tvd_top = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_strat_infos", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_strat_infos_drilling_reports_drilling_repor~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_strat_infos_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_survey_stations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    md = table.Column<double>(type: "double precision", nullable: true),
                    incl = table.Column<double>(type: "double precision", nullable: true),
                    azi = table.Column<double>(type: "double precision", nullable: true),
                    tvd = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_survey_stations", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_survey_stations_drilling_reports_drilling_r~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_survey_stations_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_well_tests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    test_number = table.Column<string>(type: "text", nullable: true),
                    test_type = table.Column<string>(type: "text", nullable: true),
                    dtim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    md_top = table.Column<double>(type: "double precision", nullable: true),
                    md_bottom = table.Column<double>(type: "double precision", nullable: true),
                    choke_orifice_size = table.Column<double>(type: "double precision", nullable: true),
                    flow_rate_oil = table.Column<double>(type: "double precision", nullable: true),
                    flow_rate_gas = table.Column<double>(type: "double precision", nullable: true),
                    flow_rate_water = table.Column<double>(type: "double precision", nullable: true),
                    gas_oil_ratio = table.Column<double>(type: "double precision", nullable: true),
                    water_oil_ratio = table.Column<double>(type: "double precision", nullable: true),
                    vol_oil_total = table.Column<double>(type: "double precision", nullable: true),
                    vol_gas_total = table.Column<double>(type: "double precision", nullable: true),
                    vol_water_total = table.Column<double>(type: "double precision", nullable: true),
                    vol_oil_stored = table.Column<double>(type: "double precision", nullable: true),
                    pres_shut_in = table.Column<double>(type: "double precision", nullable: true),
                    pres_flowing = table.Column<double>(type: "double precision", nullable: true),
                    pres_bottom = table.Column<double>(type: "double precision", nullable: true),
                    density_oil = table.Column<double>(type: "double precision", nullable: true),
                    density_gas = table.Column<double>(type: "double precision", nullable: true),
                    density_water = table.Column<double>(type: "double precision", nullable: true),
                    hydrogen_sulfide = table.Column<double>(type: "double precision", nullable: true),
                    carbon_dioxide = table.Column<double>(type: "double precision", nullable: true),
                    chloride = table.Column<double>(type: "double precision", nullable: true),
                    drilling_report_id1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_well_tests", x => x.id);
                    table.ForeignKey(
                        name: "FK_drilling_report_well_tests_drilling_reports_drilling_report~",
                        column: x => x.drilling_report_id1,
                        principalTable: "drilling_reports",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_drilling_report_well_tests_drilling_reports",
                        column: x => x.drilling_report_id,
                        principalTable: "drilling_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wellbore_survey_stations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wellbore_survey_report_id = table.Column<int>(type: "integer", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    md = table.Column<double>(type: "double precision", nullable: true),
                    incl = table.Column<double>(type: "double precision", nullable: true),
                    azim = table.Column<double>(type: "double precision", nullable: true),
                    tvd = table.Column<double>(type: "double precision", nullable: true),
                    x_offset_ew = table.Column<double>(type: "double precision", nullable: true),
                    y_offset_ns = table.Column<double>(type: "double precision", nullable: true),
                    utm_ew = table.Column<double>(type: "double precision", nullable: true),
                    utm_ns = table.Column<double>(type: "double precision", nullable: true),
                    dls = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wellbore_survey_stations", x => x.id);
                    table.ForeignKey(
                        name: "fk_wellbore_survey_stations_reports",
                        column: x => x.wellbore_survey_report_id,
                        principalTable: "wellbore_survey_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "drilling_report_bit_nozzles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drilling_report_bit_record_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence_no = table.Column<int>(type: "integer", nullable: false),
                    num_nozzle = table.Column<int>(type: "integer", nullable: true),
                    dia_nozzle = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drilling_report_bit_nozzles", x => x.id);
                    table.ForeignKey(
                        name: "fk_bit_nozzles_bit_records",
                        column: x => x.drilling_report_bit_record_id,
                        principalTable: "drilling_report_bit_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_activities_drilling_report_id_sequence_no",
                table: "drilling_report_activities",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_activities_drilling_report_id1",
                table: "drilling_report_activities",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_bit_nozzles_drilling_report_bit_record_id_s~",
                table: "drilling_report_bit_nozzles",
                columns: new[] { "drilling_report_bit_record_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_bit_records_drilling_report_id_sequence_no",
                table: "drilling_report_bit_records",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_bit_records_drilling_report_id1",
                table: "drilling_report_bit_records",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_casing_runs_drilling_report_id_sequence_no",
                table: "drilling_report_casing_runs",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_casing_runs_drilling_report_id1",
                table: "drilling_report_casing_runs",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_core_infos_drilling_report_id_sequence_no",
                table: "drilling_report_core_infos",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_core_infos_drilling_report_id1",
                table: "drilling_report_core_infos",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_equip_failures_drilling_report_id_sequence_~",
                table: "drilling_report_equip_failures",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_equip_failures_drilling_report_id1",
                table: "drilling_report_equip_failures",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_fluids_drilling_report_id_sequence_no",
                table: "drilling_report_fluids",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_fluids_drilling_report_id1",
                table: "drilling_report_fluids",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_gas_readings_drilling_report_id_sequence_no",
                table: "drilling_report_gas_readings",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_gas_readings_drilling_report_id1",
                table: "drilling_report_gas_readings",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_lith_shows_drilling_report_id_sequence_no",
                table: "drilling_report_lith_shows",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_lith_shows_drilling_report_id1",
                table: "drilling_report_lith_shows",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_log_infos_drilling_report_id_sequence_no",
                table: "drilling_report_log_infos",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_log_infos_drilling_report_id1",
                table: "drilling_report_log_infos",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_perf_infos_drilling_report_id_sequence_no",
                table: "drilling_report_perf_infos",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_perf_infos_drilling_report_id1",
                table: "drilling_report_perf_infos",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_pore_pressures_drilling_report_id_sequence_~",
                table: "drilling_report_pore_pressures",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_pore_pressures_drilling_report_id1",
                table: "drilling_report_pore_pressures",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_strat_infos_drilling_report_id_sequence_no",
                table: "drilling_report_strat_infos",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_strat_infos_drilling_report_id1",
                table: "drilling_report_strat_infos",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_survey_stations_drilling_report_id_sequence~",
                table: "drilling_report_survey_stations",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_survey_stations_drilling_report_id1",
                table: "drilling_report_survey_stations",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_well_tests_drilling_report_id_sequence_no",
                table: "drilling_report_well_tests",
                columns: new[] { "drilling_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_report_well_tests_drilling_report_id1",
                table: "drilling_report_well_tests",
                column: "drilling_report_id1");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_reports_dtim_start",
                table: "drilling_reports",
                column: "dtim_start");

            migrationBuilder.CreateIndex(
                name: "IX_drilling_reports_source_file",
                table: "drilling_reports",
                column: "source_file",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drilling_reports_wellbore_id_status_dtim",
                table: "drilling_reports",
                columns: new[] { "wellbore_id", "status_dtim" });

            migrationBuilder.CreateIndex(
                name: "IX_import_runs_started_at",
                table: "import_runs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "IX_production_daily_npd_wellbore_code",
                table: "production_daily",
                column: "npd_wellbore_code");

            migrationBuilder.CreateIndex(
                name: "IX_production_daily_production_date",
                table: "production_daily",
                column: "production_date");

            migrationBuilder.CreateIndex(
                name: "IX_production_daily_wellbore_id_production_date",
                table: "production_daily",
                columns: new[] { "wellbore_id", "production_date" });

            migrationBuilder.CreateIndex(
                name: "IX_production_monthly_npd_wellbore_code",
                table: "production_monthly",
                column: "npd_wellbore_code");

            migrationBuilder.CreateIndex(
                name: "IX_production_monthly_wellbore_id_year_month",
                table: "production_monthly",
                columns: new[] { "wellbore_id", "year", "month" });

            migrationBuilder.CreateIndex(
                name: "IX_wellbore_survey_reports_source_file",
                table: "wellbore_survey_reports",
                column: "source_file",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wellbore_survey_reports_wellbore_id",
                table: "wellbore_survey_reports",
                column: "wellbore_id");

            migrationBuilder.CreateIndex(
                name: "IX_wellbore_survey_stations_wellbore_survey_report_id_md",
                table: "wellbore_survey_stations",
                columns: new[] { "wellbore_survey_report_id", "md" });

            migrationBuilder.CreateIndex(
                name: "IX_wellbore_survey_stations_wellbore_survey_report_id_sequence~",
                table: "wellbore_survey_stations",
                columns: new[] { "wellbore_survey_report_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wellbores_npd_wellbore_code",
                table: "wellbores",
                column: "npd_wellbore_code");

            migrationBuilder.CreateIndex(
                name: "IX_wellbores_well_id_name",
                table: "wellbores",
                columns: new[] { "well_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wells_name",
                table: "wells",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drilling_report_activities");

            migrationBuilder.DropTable(
                name: "drilling_report_bit_nozzles");

            migrationBuilder.DropTable(
                name: "drilling_report_casing_runs");

            migrationBuilder.DropTable(
                name: "drilling_report_core_infos");

            migrationBuilder.DropTable(
                name: "drilling_report_equip_failures");

            migrationBuilder.DropTable(
                name: "drilling_report_fluids");

            migrationBuilder.DropTable(
                name: "drilling_report_gas_readings");

            migrationBuilder.DropTable(
                name: "drilling_report_lith_shows");

            migrationBuilder.DropTable(
                name: "drilling_report_log_infos");

            migrationBuilder.DropTable(
                name: "drilling_report_perf_infos");

            migrationBuilder.DropTable(
                name: "drilling_report_pore_pressures");

            migrationBuilder.DropTable(
                name: "drilling_report_strat_infos");

            migrationBuilder.DropTable(
                name: "drilling_report_survey_stations");

            migrationBuilder.DropTable(
                name: "drilling_report_well_tests");

            migrationBuilder.DropTable(
                name: "import_runs");

            migrationBuilder.DropTable(
                name: "production_daily");

            migrationBuilder.DropTable(
                name: "production_monthly");

            migrationBuilder.DropTable(
                name: "wellbore_survey_stations");

            migrationBuilder.DropTable(
                name: "drilling_report_bit_records");

            migrationBuilder.DropTable(
                name: "wellbore_survey_reports");

            migrationBuilder.DropTable(
                name: "drilling_reports");

            migrationBuilder.DropTable(
                name: "wellbores");

            migrationBuilder.DropTable(
                name: "wells");
        }
    }
}
