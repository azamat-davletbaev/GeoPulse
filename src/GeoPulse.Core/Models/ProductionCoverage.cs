namespace GeoPulse.Core.Models
{
    /// <summary>Покрытие скважины данными добычи: границы периода и число суточных записей.</summary>
    public sealed class ProductionCoverage
    {
        /// <summary>Первая дата с данными.</summary>
        public DateOnly FirstDate { get; init; }

        /// <summary>Последняя дата с данными.</summary>
        public DateOnly LastDate { get; init; }

        /// <summary>Количество суточных записей.</summary>
        public int DaysCount { get; init; }
    }
}
