using GeoPulse.Core.Models;

namespace GeoPulse.Core.Repositories
{
    /// <summary>Репозиторий помесячных агрегатов добычи (схема analytics).</summary>
    public interface IAnalyticsRepository
    {
        /// <summary>Помесячные агрегаты одной скважины, по возрастанию года и месяца.</summary>
        Task<IReadOnlyList<MonthlyProduction>> GetMonthlyByWellAsync(
            int wellId,
            CancellationToken cancellationToken = default);

        /// <summary>Суммарная добыча по всем скважинам за всё время; null — данных нет.</summary>
        Task<ProductionSummary?> GetSummaryAsync(CancellationToken cancellationToken = default);
    }
}
