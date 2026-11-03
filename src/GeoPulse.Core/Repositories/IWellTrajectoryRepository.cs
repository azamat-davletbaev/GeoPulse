namespace GeoPulse.Core.Repositories
{
    using GeoPulse.Core.Models;

    /// <summary>Репозиторий траекторий стволов.</summary>
    public interface IWellTrajectoryRepository
    {
        /// <summary>Замеры траектории скважины по возрастанию MD.</summary>
        Task<IReadOnlyList<WellTrajectory>> GetByWellIdAsync(int wellId, CancellationToken cancellationToken = default);
    }
}
