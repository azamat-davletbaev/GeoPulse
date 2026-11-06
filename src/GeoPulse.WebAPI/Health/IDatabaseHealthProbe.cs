namespace GeoPulse.WebAPI.Health
{
    /// <summary>
    /// Проба доступности основной БД для /health. Абстракция, чтобы контроллер не зависел
    /// от EF Core и мог быть проверен подменой пробы в тестах.
    /// </summary>
    public interface IDatabaseHealthProbe
    {
        /// <summary>true — соединение с PostgreSQL устанавливается.</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
    }
}
