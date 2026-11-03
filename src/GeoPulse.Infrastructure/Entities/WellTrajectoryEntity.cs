namespace GeoPulse.Infrastructure.Entities
{
    /// <summary>EF-сущность замера траектории ствола (таблица <c>well_trajectories</c>).</summary>
    public class WellTrajectoryEntity
    {
        /// <summary>FK → wells.Id (часть составного PK).</summary>
        public int WellId { get; set; }

        /// <summary>Measured Depth, м (часть составного PK — замеры траектории уникальны по MD).</summary>
        public double Md { get; set; }

        /// <summary>True Vertical Depth, м.</summary>
        public double Tvd { get; set; }

        /// <summary>Зенитный угол, градусы.</summary>
        public double Inclination { get; set; }

        /// <summary>Азимут, градусы.</summary>
        public double Azimuth { get; set; }

        /// <summary>Easting (ED50 / UTM 31N), м.</summary>
        public double Easting { get; set; }

        /// <summary>Northing (ED50 / UTM 31N), м.</summary>
        public double Northing { get; set; }

        /// <summary>Скважина (навигация).</summary>
        public WellEntity? Well { get; set; }
    }
}
