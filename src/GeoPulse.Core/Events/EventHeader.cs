namespace GeoPulse.Core.Events
{
    /// <summary>Заголовок события для диспетчеризации по типу и версии схемы.</summary>
    public sealed record EventHeader
    {
        /// <summary>Тип события (eventType).</summary>
        public string EventType { get; init; } = string.Empty;

        /// <summary>Версия схемы (schemaVersion).</summary>
        public int SchemaVersion { get; init; }
    }
}
