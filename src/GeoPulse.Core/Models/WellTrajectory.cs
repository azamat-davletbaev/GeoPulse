namespace GeoPulse.Core.Models
{
    /// <summary>Один замер траектории ствола (хранится в ED50/UTM31N; в WGS84 конвертится на API).</summary>
    public sealed class WellTrajectory
    {
        /// <summary>Measured Depth, м.</summary>
        public double Md { get; init; }

        /// <summary>True Vertical Depth, м.</summary>
        public double Tvd { get; init; }

        /// <summary>Зенитный угол, градусы.</summary>
        public double Inclination { get; init; }

        /// <summary>Азимут, градусы.</summary>
        public double Azimuth { get; init; }

        /// <summary>Easting (ED50 / UTM 31N), м.</summary>
        public double Easting { get; init; }

        /// <summary>Northing (ED50 / UTM 31N), м.</summary>
        public double Northing { get; init; }
    }
}
