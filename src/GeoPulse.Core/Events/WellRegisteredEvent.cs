namespace GeoPulse.Core.Events
{
    /// <summary>Событие регистрации скважины (well.registered).</summary>
    public sealed record WellRegisteredEvent
    {
        public string EventType { get; init; } = EventTypes.WellRegistered;

        public int SchemaVersion { get; init; } = EventTypes.SchemaVersion;

        public WellRegisteredData Data { get; init; } = new();
    }

    /// <summary>Данные события well.registered.</summary>
    public sealed class WellRegisteredData
    {
        public int NpdWellBoreCode { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Field { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }
}
