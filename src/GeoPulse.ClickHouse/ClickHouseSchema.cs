using ClickHouse.Client.ADO;

namespace GeoPulse.ClickHouse
{
    /// <summary>
    /// Единое описание схемы ClickHouse GeoPulse (база <c>gp</c>) и её идемпотентное создание.
    /// До этого DDL был продублирован в CHIngest (база, wells, production_daily) и Aggregator
    /// (база, production_daily, monthly_production) — две копии одной схемы легко расходятся.
    /// С ADR-016 предвычисленной таблицы агрегатов нет: помесячные суммы считает WebAPI на лету.
    /// </summary>
    public static class ClickHouseSchema
    {
        /// <summary>Имя базы ClickHouse.</summary>
        public const string Database = "gp";

        /// <summary>Таблица сырых суточных событий.</summary>
        public const string ProductionDailyTable = "gp.production_daily";

        /// <summary>Справочник скважин в ClickHouse.</summary>
        public const string WellsTable = "gp.wells";

        /// <summary>Создание базы, если её нет.</summary>
        public const string CreateDatabaseSql = "CREATE DATABASE IF NOT EXISTS gp";

        /// <summary>
        /// Справочник стволов: заполняется из событий well.registered.
        /// Дедупликация — по ключу сортировки (npd_well_bore_code).
        /// </summary>
        public const string CreateWellsSql =
            "CREATE TABLE IF NOT EXISTS gp.wells (" +
            "npd_well_bore_code UInt32," +
            "name String," +
            "field String," +
            "type String," +
            "well_id UInt32" +
            ") ENGINE = ReplacingMergeTree ORDER BY npd_well_bore_code";

        /// <summary>
        /// Сырые суточные записи. Читать обязательно с FINAL: дедупликация повторов топика
        /// выполняется движком по (npd_well_bore_code, date).
        /// </summary>
        public const string CreateProductionDailySql =
            "CREATE TABLE IF NOT EXISTS gp.production_daily (" +
            "well_id UInt32," +
            "npd_well_bore_code UInt32," +
            "date Date," +
            "oil_vol Float64," +
            "gas_vol Float64," +
            "water_vol Float64," +
            "water_injection_vol Float64," +
            "on_stream_hrs Float64," +
            "avg_downhole_pressure Float64," +
            "avg_downhole_temperature Float64," +
            "avg_dp_tubing Float64," +
            "avg_annulus_pressure Float64," +
            "avg_choke_size Float64," +
            "avg_whp Float64," +
            "avg_wht Float64," +
            "dp_choke_size Float64" +
            ") ENGINE = ReplacingMergeTree ORDER BY (npd_well_bore_code, date)";

        /// <summary>
        /// Выполняет переданные DDL-операторы в одном соединении в указанном порядке.
        /// Все операторы идемпотентны (IF NOT EXISTS), поэтому сервисы вызывают метод при старте.
        /// </summary>
        /// <param name="connectionString">Строка подключения ClickHouse.</param>
        /// <param name="statements">DDL-операторы в порядке применения.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static async Task EnsureAsync(
            string connectionString,
            IReadOnlyList<string> statements,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new ClickHouseConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            foreach (var statement in statements)
            {
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
