namespace GeoPulse.Infrastructure
{
    /// <summary>Параметры подключения к основной БД PostgreSQL.</summary>
    public static class DatabaseConfiguration
    {
        /// <summary>Имя строки подключения в конфигурации.</summary>
        public const string ConnectionStringName = "CoreDb";

        /// <summary>Строка подключения по умолчанию (переопределяется env-переменной ConnectionStrings__CoreDb).</summary>
        public const string DefaultConnectionString =
            "Host=localhost;Port=5432;Database=geopulse_core;Username=geopulse;Password=geopulse";
    }
}
