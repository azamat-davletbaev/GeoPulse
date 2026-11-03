namespace GeoPulse.WebAPI.Models
{
    /// <summary>Замер траектории в WGS84 (координаты конвертированы из ED50/UTM31N на бэкенде).</summary>
    public record TrajectoryPointDto
    {
        /// <summary>Measured Depth, м.</summary>
        public double Md { get; init; }

        /// <summary>True Vertical Depth, м.</summary>
        public double Tvd { get; init; }

        /// <summary>Зенитный угол, градусы.</summary>
        public double Inclination { get; init; }

        /// <summary>Азимут, градусы.</summary>
        public double Azimuth { get; init; }

        /// <summary>Широта WGS84, градусы.</summary>
        public double Latitude { get; init; }

        /// <summary>Долгота WGS84, градусы.</summary>
        public double Longitude { get; init; }
    }
}
