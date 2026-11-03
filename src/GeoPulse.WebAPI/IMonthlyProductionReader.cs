using GeoPulse.WebAPI.Models;

namespace GeoPulse.WebAPI
{
    /// <summary>
    /// Абстракция чтения помесячной аналитики из ClickHouse. Отделена от конкретного
    /// <see cref="ClickHouseAnalyticsService"/>, чтобы юнит-тесты ReportService могли подменять
    /// источник данных без обращения к реальному ClickHouse.
    /// </summary>
    public interface IMonthlyProductionReader
    {
        /// <summary>Все помесячные агрегаты по всем скважинам, по возрастанию года и месяца.</summary>
        Task<IReadOnlyList<MonthlyProductionFlatDto>> GetAllMonthlyAsync(CancellationToken cancellationToken = default);

        /// <summary>Суммарная добыча по всем скважинам за всё время.</summary>
        Task<ProductionSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    }
}
