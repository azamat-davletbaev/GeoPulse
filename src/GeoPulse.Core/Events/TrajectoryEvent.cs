namespace GeoPulse.Core.Events
{
    /// <summary>Событие траектории ствола (well.trajectory): один event на ствол с полным набором замеров.</summary>
    public sealed record TrajectoryEvent
    {
        public string EventType { get; init; } = EventTypes.Trajectory;

        public int SchemaVersion { get; init; } = EventTypes.SchemaVersion;

        public TrajectoryData Data { get; init; } = new();
    }

    /// <summary>Данные события well.trajectory (без внутреннего wellId — только натуральный ключ).</summary>
    public sealed class TrajectoryData
    {
        /// <summary>Код ствола в НПД (натуральный ключ).</summary>
        public int NpdWellBoreCode { get; init; }

        /// <summary>Забой по стволу (Bottom Hole MD из заголовка выгрузки), м.</summary>
        public double? TdMd { get; init; }

        /// <summary>Забой по вертикали (Bottom Hole TVD из заголовка выгрузки), м.</summary>
        public double? TdTvd { get; init; }

        /// <summary>Замеры траектории по возрастанию MD.</summary>
        public IReadOnlyList<TrajectoryPointData> Points { get; init; } = Array.Empty<TrajectoryPointData>();
    }

    /// <summary>Один замер траектории (MD, TVD, инклинометрия и координаты ED50/UTM31N).</summary>
    public sealed record TrajectoryPointData
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
