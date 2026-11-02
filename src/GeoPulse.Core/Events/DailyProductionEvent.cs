namespace GeoPulse.Core.Events
{
    /// <summary>Событие суточной добычи (production.daily).</summary>
    public sealed record DailyProductionEvent
    {
        public string EventType { get; init; } = EventTypes.ProductionDaily;

        public int SchemaVersion { get; init; } = EventTypes.SchemaVersion;

        public DailyProductionData Data { get; init; } = new();
    }

    /// <summary>Данные события production.daily (без внутреннего wellId — только натуральный ключ).</summary>
    public sealed record DailyProductionData
    {
        public int NpdWellBoreCode { get; init; }

        public DateOnly Date { get; init; }

        public double? OnStreamHrs { get; init; }

        public double? AvgDownholePressure { get; init; }

        public double? AvgDownholeTemperature { get; init; }

        public double? AvgDpTubing { get; init; }

        public double? AvgAnnulusPressure { get; init; }

        public double? AvgChokeSize { get; init; }

        public double? AvgWhp { get; init; }

        public double? AvgWht { get; init; }

        public double? DpChokeSize { get; init; }

        public double? OilVol { get; init; }

        public double? GasVol { get; init; }

        public double? WaterVol { get; init; }

        public double? WaterInjectionVol { get; init; }
    }
}
