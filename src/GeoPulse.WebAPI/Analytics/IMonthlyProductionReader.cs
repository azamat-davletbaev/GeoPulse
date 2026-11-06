using GeoPulse.Core.Models;

namespace GeoPulse.WebAPI.Analytics
{
    /// <summary>
    /// Порт чтения помесячной аналитики (реализация — ClickHouse). Контроллеры и отчёты зависят
    /// от этого интерфейса, а не от конкретного сервиса: источник можно подменить в тестах.
    /// </summary>
    public interface IMonthlyProductionReader
    {
        /// <summary>Помесячные агрегаты одной скважины, по возрастанию года и месяца.</summary>
        Task<IReadOnlyList<MonthlyProduction>> GetMonthlyByWellAsync(
            int wellId,
            CancellationToken cancellationToken = default);

        /// <summary>Все помесячные агрегаты по всем скважинам (для отчётов), по возрастанию года и месяца.</summary>
        Task<IReadOnlyList<MonthlyProduction>> GetAllMonthlyAsync(CancellationToken cancellationToken = default);

        /// <summary>Суммарная добыча по всем скважинам за всё время.</summary>
        Task<ProductionSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
    }
}
