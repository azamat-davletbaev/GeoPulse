using System.Globalization;
using System.Text;
using System.Text.Json;
using ClickHouse.Client.ADO;
using Confluent.Kafka;
using GeoPulse.Core.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GeoPulse.CHIngest
{
    /// <summary>
    /// Третий читатель топика production-daily: пишет сырые данные в ClickHouse
    /// (база gp, таблицы wells и production_daily на движке ReplacingMergeTree).
    /// Дедупликация дубликатов топика — средствами ReplacingMergeTree (чтение через FINAL),
    /// без дедупов в памяти. Резолв npdWellBoreCode → well_id — по PostgreSQL wells (кэш на старте).
    /// </summary>
    public sealed class CHIngestService : BackgroundService
    {
        private const string ConsumerGroup = "geopulse-chingest";

        private const string CreateDatabaseSql = "CREATE DATABASE IF NOT EXISTS gp";

        private const string CreateWellsSql =
            "CREATE TABLE IF NOT EXISTS gp.wells (" +
            "npd_well_bore_code UInt32," +
            "name String," +
            "field String," +
            "type String," +
            "well_id UInt32" +
            ") ENGINE = ReplacingMergeTree ORDER BY npd_well_bore_code";

        private const string CreateProductionDailySql =
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

        private readonly IConfiguration _configuration;
        private readonly ILogger<CHIngestService> _logger;
        private readonly IProducer<string, string> _dlqProducer;

        /// <summary>Конструктор.</summary>
        public CHIngestService(
            IConfiguration configuration,
            ILogger<CHIngestService> logger,
            IProducer<string, string> dlqProducer)
        {
            _configuration = configuration;
            _logger = logger;
            _dlqProducer = dlqProducer;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var clickHouseConnectionString = _configuration["ClickHouse:ConnectionString"]
                ?? throw new InvalidOperationException("Не задан ClickHouse:ConnectionString в appsettings.json.");
            var postgresConnectionString = _configuration.GetConnectionString("CoreDb")
                ?? throw new InvalidOperationException("Не задана ConnectionStrings:CoreDb в appsettings.json.");
            var bootstrapServers = _configuration["Kafka:BootstrapServers"]
                ?? throw new InvalidOperationException("Не задан Kafka:BootstrapServers в appsettings.json.");
            var topic = _configuration["Kafka:Topic"] ?? "production-daily";
            var dlqTopic = _configuration["Kafka:DlqTopic"] ?? "production-daily-dlq";
            var batchSize = _configuration.GetValue("Kafka:BatchSize", 500);
            if (batchSize <= 0)
            {
                batchSize = 500;
            }

            await EnsureSchemaAsync(clickHouseConnectionString, stoppingToken);

            var wellIdsByNpd = await LoadWellIdsAsync(postgresConnectionString, stoppingToken);
            _logger.LogInformation("Кэш скважин загружен: {Count}", wellIdsByNpd.Count);

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = ConsumerGroup,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            consumer.Subscribe(topic);

            var batch = new List<DailyRow>();
            var counters = new Counters();

            _logger.LogInformation("CHIngest запущен: топик {Topic}, группа {Group}", topic, ConsumerGroup);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = consumer.Consume(TimeSpan.FromMilliseconds(250));
                    if (result is null)
                    {
                        await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);
                        continue;
                    }

                    counters.Received++;

                    var classification = Classify(result.Message.Value);

                    switch (classification.Kind)
                    {
                        case MessageKind.Well:
                        {
                            await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);

                            var well = classification.Well!;
                            if (!wellIdsByNpd.TryGetValue(well.NpdWellBoreCode, out var wellId))
                            {
                                await PublishDlqAsync(dlqTopic, result, "unknown.well");
                                counters.ToDlq++;
                                consumer.Commit(result);
                                break;
                            }

                            await InsertWellAsync(clickHouseConnectionString, well, wellId, stoppingToken);
                            counters.Wells++;
                            consumer.Commit(result);
                            break;
                        }

                        case MessageKind.Daily:
                        {
                            var daily = classification.Daily!;
                            if (!wellIdsByNpd.TryGetValue(daily.NpdWellBoreCode, out var wellId))
                            {
                                await PublishDlqAsync(dlqTopic, result, "unknown.well");
                                counters.ToDlq++;
                                consumer.Commit(result);
                                break;
                            }

                            batch.Add(ToDailyRow(wellId, daily));

                            if (batch.Count >= batchSize)
                            {
                                await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);
                            }

                            break;
                        }

                        default:
                        {
                            await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);

                            await PublishDlqAsync(dlqTopic, result, classification.Error ?? "invalid.event");
                            counters.ToDlq++;
                            consumer.Commit(result);
                            break;
                        }
                    }
                }
            }
            finally
            {
                await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);

                try
                {
                    consumer.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка при закрытии консьюмера");
                }

                _dlqProducer.Flush(TimeSpan.FromSeconds(10));

                _logger.LogInformation(
                    "CHIngest завершён: получено {Received}, скважин {Wells}, записано {Inserted}, батчей {Batches}, в DLQ {Dlq}",
                    counters.Received, counters.Wells, counters.Inserted, counters.Batches, counters.ToDlq);
            }
        }

        /// <summary>Создаёт базу gp и таблицы wells / production_daily в ClickHouse.</summary>
        private static async Task EnsureSchemaAsync(string connectionString, CancellationToken ct)
        {
            await using var connection = new ClickHouseConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();

            command.CommandText = CreateDatabaseSql;
            await command.ExecuteNonQueryAsync(ct);

            command.CommandText = CreateWellsSql;
            await command.ExecuteNonQueryAsync(ct);

            command.CommandText = CreateProductionDailySql;
            await command.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Загружает кэш npdWellBoreCode → well_id из PostgreSQL wells.</summary>
        private static async Task<Dictionary<int, int>> LoadWellIdsAsync(string connectionString, CancellationToken ct)
        {
            var result = new Dictionary<int, int>();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT npd_well_bore_code, id FROM wells", connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result[reader.GetInt32(0)] = reader.GetInt32(1);
            }

            return result;
        }

        /// <summary>Сбрасывает батч добычи одним INSERT-ом и коммитит офсеты всех партиций.</summary>
        private async Task FlushDailyBatchAsync(
            string connectionString,
            List<DailyRow> batch,
            IConsumer<string, string> consumer,
            Counters counters,
            CancellationToken ct)
        {
            if (batch.Count == 0)
            {
                return;
            }

            await InsertDailyBatchAsync(connectionString, batch, ct);
            counters.Inserted += batch.Count;
            counters.Batches++;
            batch.Clear();

            consumer.Commit();

            _logger.LogInformation(
                "CHIngest: получено {Received}, записано {Inserted}, батчей {Batches}",
                counters.Received, counters.Inserted, counters.Batches);
        }

        /// <summary>Апсертит строку скважины в gp.wells (ReplacingMergeTree сам схлопнет дубли по ключу).</summary>
        private static async Task InsertWellAsync(
            string connectionString,
            WellRegisteredData well,
            int wellId,
            CancellationToken ct)
        {
            await using var connection = new ClickHouseConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO gp.wells (npd_well_bore_code, name, field, type, well_id) VALUES (" +
                well.NpdWellBoreCode.ToString(CultureInfo.InvariantCulture) + ", " +
                EscapeString(well.Name) + ", " +
                EscapeString(well.Field) + ", " +
                EscapeString(well.Type) + ", " +
                wellId.ToString(CultureInfo.InvariantCulture) + ")";

            await command.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Вставляет батч добычи одним многострочным INSERT-ом.</summary>
        private static async Task InsertDailyBatchAsync(
            string connectionString,
            IReadOnlyList<DailyRow> batch,
            CancellationToken ct)
        {
            await using var connection = new ClickHouseConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();

            var sql = new StringBuilder(batch.Count * 160 + 256);
            sql.Append("INSERT INTO gp.production_daily (well_id, npd_well_bore_code, date, oil_vol, gas_vol, water_vol, water_injection_vol, on_stream_hrs, avg_downhole_pressure, avg_downhole_temperature, avg_dp_tubing, avg_annulus_pressure, avg_choke_size, avg_whp, avg_wht, dp_choke_size) VALUES ");

            for (var i = 0; i < batch.Count; i++)
            {
                if (i > 0)
                {
                    sql.Append(',');
                }

                var row = batch[i];
                sql.Append('(');
                sql.Append(row.WellId.ToString(CultureInfo.InvariantCulture)).Append(", ");
                sql.Append(row.NpdWellBoreCode.ToString(CultureInfo.InvariantCulture)).Append(", ");
                sql.Append('\'').Append(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("', ");
                sql.Append(Float(row.OilVol)).Append(", ");
                sql.Append(Float(row.GasVol)).Append(", ");
                sql.Append(Float(row.WaterVol)).Append(", ");
                sql.Append(Float(row.WaterInjectionVol)).Append(", ");
                sql.Append(Float(row.OnStreamHrs)).Append(", ");
                sql.Append(Float(row.AvgDownholePressure)).Append(", ");
                sql.Append(Float(row.AvgDownholeTemperature)).Append(", ");
                sql.Append(Float(row.AvgDpTubing)).Append(", ");
                sql.Append(Float(row.AvgAnnulusPressure)).Append(", ");
                sql.Append(Float(row.AvgChokeSize)).Append(", ");
                sql.Append(Float(row.AvgWhp)).Append(", ");
                sql.Append(Float(row.AvgWht)).Append(", ");
                sql.Append(Float(row.DpChokeSize));
                sql.Append(')');
            }

            command.CommandText = sql.ToString();
            await command.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Форматирует nullable double как литерал Float64 (null → 0).</summary>
        private static string Float(double? value) =>
            value?.ToString("R", CultureInfo.InvariantCulture) ?? "0";

        /// <summary>Экранирует строковый литерал ClickHouse.</summary>
        private static string EscapeString(string value) =>
            "'" + value.Replace("'", "''") + "'";

        /// <summary>Собирает строку добычи из события, подставляя разрешённый wellId.</summary>
        private static DailyRow ToDailyRow(int wellId, DailyProductionData data) => new()
        {
            WellId = (uint)wellId,
            NpdWellBoreCode = (uint)data.NpdWellBoreCode,
            Date = data.Date,
            OilVol = data.OilVol,
            GasVol = data.GasVol,
            WaterVol = data.WaterVol,
            WaterInjectionVol = data.WaterInjectionVol,
            OnStreamHrs = data.OnStreamHrs,
            AvgDownholePressure = data.AvgDownholePressure,
            AvgDownholeTemperature = data.AvgDownholeTemperature,
            AvgDpTubing = data.AvgDpTubing,
            AvgAnnulusPressure = data.AvgAnnulusPressure,
            AvgChokeSize = data.AvgChokeSize,
            AvgWhp = data.AvgWhp,
            AvgWht = data.AvgWht,
            DpChokeSize = data.DpChokeSize
        };

        /// <summary>Отправляет оригинальное сообщение в DLQ-топик с причиной в заголовке.</summary>
        private async Task PublishDlqAsync(string dlqTopic, ConsumeResult<string, string> result, string reason)
        {
            var message = new Message<string, string>
            {
                Key = result.Message.Key,
                Value = result.Message.Value,
                Headers = new Headers
                {
                    { "reason", Encoding.UTF8.GetBytes(reason) },
                    { "source-topic", Encoding.UTF8.GetBytes(result.Topic) }
                }
            };

            try
            {
                await _dlqProducer.ProduceAsync(dlqTopic, message);
                _logger.LogInformation("DLQ: reason={Reason}, key={Key}", reason, result.Message.Key);
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Ошибка отправки в DLQ-топик {Topic}", dlqTopic);
            }
        }

        private enum MessageKind
        {
            Well,
            Daily,
            Invalid
        }

        private sealed record Classification(MessageKind Kind, WellRegisteredData? Well, DailyProductionData? Daily, string? Error);

        /// <summary>Разбирает сообщение: тип/версия схемы и данные события.</summary>
        private static Classification Classify(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new Classification(MessageKind.Invalid, null, null, "empty.message");
            }

            EventHeader header;
            try
            {
                header = JsonSerializer.Deserialize<EventHeader>(raw, MessagingJson.Options) ?? new EventHeader();
            }
            catch (JsonException)
            {
                return new Classification(MessageKind.Invalid, null, null, "unparseable.json");
            }

            if (header.SchemaVersion != EventTypes.SchemaVersion)
            {
                return new Classification(MessageKind.Invalid, null, null, "unknown.schemaVersion");
            }

            switch (header.EventType)
            {
                case EventTypes.WellRegistered:
                {
                    try
                    {
                        var evt = JsonSerializer.Deserialize<WellRegisteredEvent>(raw, MessagingJson.Options);
                        return evt?.Data is null
                            ? new Classification(MessageKind.Invalid, null, null, "missing.data")
                            : new Classification(MessageKind.Well, evt.Data, null, null);
                    }
                    catch (JsonException)
                    {
                        return new Classification(MessageKind.Invalid, null, null, "unparseable.well.event");
                    }
                }

                case EventTypes.ProductionDaily:
                {
                    try
                    {
                        var evt = JsonSerializer.Deserialize<DailyProductionEvent>(raw, MessagingJson.Options);
                        return evt?.Data is null
                            ? new Classification(MessageKind.Invalid, null, null, "missing.data")
                            : new Classification(MessageKind.Daily, null, evt.Data, null);
                    }
                    catch (JsonException)
                    {
                        return new Classification(MessageKind.Invalid, null, null, "unparseable.daily.event");
                    }
                }

                default:
                    return new Classification(MessageKind.Invalid, null, null, $"unknown.eventType:{header.EventType}");
            }
        }

        /// <summary>Счётчики обработки за один прогон CHIngest.</summary>
        private sealed class Counters
        {
            public long Received;
            public long Wells;
            public long Inserted;
            public long Batches;
            public long ToDlq;
        }

        /// <summary>Строка таблицы gp.production_daily.</summary>
        private sealed class DailyRow
        {
            public uint WellId { get; init; }
            public uint NpdWellBoreCode { get; init; }
            public DateOnly Date { get; init; }
            public double? OilVol { get; init; }
            public double? GasVol { get; init; }
            public double? WaterVol { get; init; }
            public double? WaterInjectionVol { get; init; }
            public double? OnStreamHrs { get; init; }
            public double? AvgDownholePressure { get; init; }
            public double? AvgDownholeTemperature { get; init; }
            public double? AvgDpTubing { get; init; }
            public double? AvgAnnulusPressure { get; init; }
            public double? AvgChokeSize { get; init; }
            public double? AvgWhp { get; init; }
            public double? AvgWht { get; init; }
            public double? DpChokeSize { get; init; }
        }
    }
}
