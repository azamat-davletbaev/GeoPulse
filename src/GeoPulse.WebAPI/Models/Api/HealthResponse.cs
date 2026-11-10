namespace GeoPulse.WebAPI.Models.Api
{
    /// <summary>Состояние API и его зависимости — базы данных PostgreSQL.</summary>
    public record HealthResponse
    {
        /// <summary>Итоговый статус: <c>ok</c> или <c>degraded</c>.</summary>
        public string Status { get; init; } = "ok";

        /// <summary>Доступность PostgreSQL: <c>up</c> или <c>down</c>.</summary>
        public string Database { get; init; } = "up";

        /// <summary>Момент проверки (UTC).</summary>
        public DateTimeOffset CheckedAtUtc { get; init; }
    }
}
