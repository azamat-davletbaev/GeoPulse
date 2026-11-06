using System.Globalization;
using System.Text;
using ClickHouse.Client.ADO;
using Confluent.Kafka;
using GeoPulse.ClickHouse;
using GeoPulse.Core.Events;
using GeoPulse.Core.Repositories;
using GeoPulse.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeoPulse.CHIngest
{
    /// <summary>
    /// Второй читатель топика production-daily: пишет сырые данные в ClickHouse
    /// (база gp, таблицы wells и production_daily на движке ReplacingMergeTree).
    /// Дедупликация дубликатов топика — средствами ReplacingMergeTree (чтение через FINAL),
    /// без дедупов в памяти. Резолв npdWellBoreCode → well_id — по PostgreSQL wells (общий кэш).
    /// </summary>
    public sealed class CHIngestService : BackgroundService
    {
        private const string ConsumerGroup = "geopulse-chingest";

        /// <summary>Пауза перед повтором после ошибки чтения из Kafka (топик не создан, брокер недоступен).</summary>
        private const int ConsumeErrorDelayMs = 1_000;

        /// <summary>DDL, который обеспечивает сам CHIngest (справочник и сырые суточные данные).</summary>
        private static readonly string[] SchemaStatements =
        {
            ClickHouseSchema.CreateDatabaseSql,
            ClickHouseSchema.CreateWellsSql,
            ClickHouseSchema.CreateProductionDailySql
        };

        private readonly IConfiguration _configuration;
        private readonly ILogger<CHIngestService> _logger;
        private readonly DlqPublisher _dlq;
        private readonly IWellIdResolver _wellIds;
        private readonly TopicProvisioner _topics;

        /// <summary>Конструктор.</summary>
        public CHIngestService(
            IConfiguration configuration,
            ILogger<CHIngestService> logger,
            DlqPublisher dlq,
            IWellIdResolver wellIds,
            TopicProvisioner topics)
        {
            _configuration = configuration;
            _logger = logger;
            _dlq = dlq;
            _wellIds = wellIds;
            _topics = topics;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var clickHouseConnectionString = _configuration["ClickHouse:ConnectionString"]
                ?? throw new InvalidOperationException("Не задан ClickHouse:ConnectionString в appsettings.json.");
            var bootstrapServers = _configuration["Kafka:BootstrapServers"]
                ?? throw new InvalidOperationException("Не задан Kafka:BootstrapServers в appsettings.json.");
            var topic = _configuration["Kafka:Topic"] ?? "production-daily";
            var dlqTopic = _configuration["Kafka:DlqTopic"] ?? "production-daily-dlq";
            var batchSize = _configuration.GetValue("Kafka:BatchSize", 500);
            if (batchSize <= 0)
            {
                batchSize = 500;
            }

            await ClickHouseSchema.EnsureAsync(clickHouseConnectionString, SchemaStatements, stoppingToken);

            // Топик создаём явно: Kafka не создаёт его по подписке консьюмера (KIP-361).
            var partitions = _configuration.GetValue("Kafka:TopicPartitions", 1);
            var replicationFactor = _configuration.GetValue("Kafka:ReplicationFactor", 1);
            await _topics.EnsureAsync(
                bootstrapServers,
                topic,
                partitions <= 0 ? 1 : partitions,
                (short)(replicationFactor <= 0 ? 1 : replicationFactor),
                stoppingToken);

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = ConsumerGroup,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
                // Фолбэк для брокеров, где автосоздание по подписке действительно работает;
                // основной путь — явное создание топика через TopicProvisioner (см. ADR-015).
                AllowAutoCreateTopics = true
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
                    ConsumeResult<string, string>? result;
                    try
                    {
                        result = consumer.Consume(TimeSpan.FromMilliseconds(250));
                    }
                    catch (ConsumeException ex)
                    {
                        // Топик может быть ещё не создан: не роняем хост, ждём следующей попытки.
                        _logger.LogWarning(
                            "Ошибка чтения из топика {Topic}: {Reason}. Повтор через {DelayMs} мс",
                            topic,
                            ex.Error.Reason,
                            ConsumeErrorDelayMs);

                        try
                        {
                            await Task.Delay(ConsumeErrorDelayMs, stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        continue;
                    }

                    if (result is null)
                    {
                        await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);
                        continue;
                    }

                    counters.Received++;

                    var classification = EventClassifier.Classify(result.Message.Value);

                    switch (classification.Kind)
                    {
                        case MessageKind.Well:
                            {
                                await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);

                                var well = classification.Well!;
                                var wellId = await _wellIds.ResolveAsync(well.NpdWellBoreCode, stoppingToken);
                                if (wellId is null)
                                {
                                    await _dlq.PublishAsync(dlqTopic, result, DlqReasons.UnknownWell, stoppingToken);
                                    counters.ToDlq++;
                                    consumer.Commit(result);
                                    break;
                                }

                                await InsertWellAsync(clickHouseConnectionString, well, wellId.Value, stoppingToken);
                                counters.Wells++;
                                consumer.Commit(result);
                                break;
                            }

                        case MessageKind.Daily:
                            {
                                var daily = classification.Daily!;
                                var wellId = await _wellIds.ResolveAsync(daily.NpdWellBoreCode, stoppingToken);
                                if (wellId is null)
                                {
                                    await _dlq.PublishAsync(dlqTopic, result, DlqReasons.UnknownWell, stoppingToken);
                                    counters.ToDlq++;
                                    consumer.Commit(result);
                                    break;
                                }

                                batch.Add(ToDailyRow(wellId.Value, daily));

                                if (batch.Count >= batchSize)
                                {
                                    await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);
                                }

                                break;
                            }

                        case MessageKind.Trajectory:
                            {
                                // Геометрия не пишется в ClickHouse (не таймсерия): сбрасываем батч добычи
                                // и подтверждаем событие, не отправляя его в DLQ.
                                await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);
                                consumer.Commit(result);
                                break;
                            }

                        default:
                            {
                                await FlushDailyBatchAsync(clickHouseConnectionString, batch, consumer, counters, stoppingToken);

                                await _dlq.PublishAsync(
                                    dlqTopic, result, classification.Error ?? DlqReasons.InvalidEvent, stoppingToken);
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

                _dlq.Flush(TimeSpan.FromSeconds(10));

                _logger.LogInformation(
                    "CHIngest завершён: получено {Received}, скважин {Wells}, записано {Inserted}, батчей {Batches}, в DLQ {Dlq}",
                    counters.Received, counters.Wells, counters.Inserted, counters.Batches, counters.ToDlq);
            }
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
