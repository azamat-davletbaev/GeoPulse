namespace GeoPulse.Core.Repositories
{
    /// <summary>
    /// Резолв натурального ключа ствола (npdWellBoreCode) во внутренний идентификатор скважины (wells.id).
    /// Общий порт для консьюмеров: справочник живёт в PostgreSQL, значение кэшируется.
    /// </summary>
    public interface IWellIdResolver
    {
        /// <summary>Возвращает well_id для кода НПД; null — скважина не зарегистрирована.</summary>
        /// <param name="npdWellBoreCode">Код ствола в НПД.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<int?> ResolveAsync(int npdWellBoreCode, CancellationToken cancellationToken = default);
    }
}
