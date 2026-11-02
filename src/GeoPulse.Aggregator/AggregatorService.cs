using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using GeoPulse.Core.Events;
using GeoPulse.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace GeoPulse.Aggregator
{
    /// <summary>
    /// Агрегатор помесячной добычи: читает топик production-daily (группа geopulse-aggregator,
    /// auto.offset.reset = earliest) и ведёт помесячные суммы по скважинам.
    /// Базовая линия загружается из analytics.monthly_production, прирост накапливается в памяти
    /// и флашится через INSERT ... ON CONFLICT (well_id, year, month) DO UPDATE, после чего
    /// коммитятся офсеты всех партиций. Дубли дней (well_id, date) в рамках процесса пропускаются.
    /// </summary>
    public sealed class AggregatorService : BackgroundService
    {
        private const string ConsumerGroup = "geopulse-aggregator";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AggregatorService> _logger;
        private readonly IProducer<string, string> _dlqProducer;

        /// <summary>Конструктор.</summary>
        public AggregatorService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<AggregatorService> logger,
            IProducer<string, string> dlqProducer)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _dlqProducer = dlqProducer;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

            await db.Database.MigrateAsync(stoppingToken);

            var bootstrapServers = _configuration["Kafka:BootstrapServers"]
                ?? throw new InvalidOperationException("Не задан Kafka:BootstrapServers в appsettings.json.");
            var topic = _configuration["Kafka:Topic"] ?? "production-daily";
            var dlqTopic = _configuration["Kafka:DlqTopic"] ?? "production-daily-dlq";
            var flushInterval = _configuration.GetValue("Kafka:FlushInterval", 500);
            if (flushInterval <= 0)
            {
                flushInterval = 500;
            }

            // Базовая линия: текущие агрегаты из БД. Прирост поверх неё записывается через overwrite.
            var totals = await LoadBaselineAsync(db, stoppingToken);
            // Кэш резолва npdWellBoreCode → well_id (загружается один раз из таблицы wells).
            var wellIdsByNpd = await LoadWellIdsAsync(db, stoppingToken);
            var dirty = new HashSet<MonthKey>();
            var processedDays = new HashSet<DayKey>();

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = ConsumerGroup,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            consumer.Subscribe(topic);

            var counters = new Counters();

            _logger.LogInformation(
                "Агрегатор запущен: топик {Topic}, группа {Group}, базовых строк {Baseline}",
                topic, ConsumerGroup, totals.Count);

            try
            {
                var processedSinceFlush = 0;
                var pendingCommit = false;

                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = consumer.Consume(TimeSpan.FromMilliseconds(250));
                    if (result is null)
                    {
                        // Топик опустел — фиксируем прогресс, если с последнего коммита были сообщения.
                        if (pendingCommit)
                        {
                            await FlushAsync(db, totals, dirty, consumer, counters, stoppingToken);
                            processedSinceFlush = 0;
                            pendingCommit = false;
                        }

                        continue;
                    }

                    counters.Received++;
                    pendingCommit = true;

                    var classification = Classify(result.Message.Value);

                    switch (classification.Kind)
                    {
                        case MessageKind.Well:
                            // well.registered — пропуск: агрегаты не меняются.
                            counters.Processed++;
                            processedSinceFlush++;
                            break;

                        case MessageKind.Daily:
                        {
                            var data = classification.Daily!;
                            if (!wellIdsByNpd.TryGetValue(data.NpdWellBoreCode, out var wellId))
                            {
                                await PublishDlqAsync(dlqTopic, result, "unknown.well");
                                counters.ToDlq++;
                                break;
                            }

                            var dayKey = new DayKey(wellId, data.Date);
                            if (processedDays.Add(dayKey))
                            {
                                ApplyDaily(totals, dirty, wellId, data);
                            }

                            counters.Processed++;
                            processedSinceFlush++;
                            break;
                        }

                        default:
                            await PublishDlqAsync(dlqTopic, result, classification.Error ?? "invalid.event");
                            counters.ToDlq++;
                            break;
                    }

                    if (processedSinceFlush >= flushInterval)
                    {
                        await FlushAsync(db, totals, dirty, consumer, counters, stoppingToken);
                        processedSinceFlush = 0;
                        pendingCommit = false;
                    }
                }
            }
            finally
            {
                await FlushAsync(db, totals, dirty, consumer, counters, stoppingToken);

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
                    "Агрегатор завершён: получено {Received}, обработано {Processed}, в DLQ {Dlq}, отфлашено батчей {Flushed}",
                    counters.Received, counters.Processed, counters.ToDlq, counters.Flushed);
            }
        }

        /// <summary>Флашит прирост в БД (upsert) и коммитит офсеты всех партиций.</summary>
        private async Task FlushAsync(
            CoreDbContext db,
            Dictionary<MonthKey, MonthlyTotal> totals,
            HashSet<MonthKey> dirty,
            IConsumer<string, string> consumer,
            Counters counters,
            CancellationToken cancellationToken)
        {
            if (dirty.Count > 0)
            {
                var rows = dirty.Select(key => totals[key]).ToList();
                await UpsertMonthlyAsync(db, rows, cancellationToken);
                dirty.Clear();
            }

            consumer.Commit();
            counters.Flushed++;

            _logger.LogInformation(
                "Агрегатор: получено {Received}, обработано {Processed}, отфлашено батчей {Flushed}",
                counters.Received, counters.Processed, counters.Flushed);
        }

        /// <summary>Загружает текущие агрегаты из БД как базовую линию.</summary>
        private static async Task<Dictionary<MonthKey, MonthlyTotal>> LoadBaselineAsync(
            CoreDbContext db,
            CancellationToken cancellationToken)
        {
            var entities = await db.MonthlyProduction.AsNoTracking().ToListAsync(cancellationToken);

            var result = new Dictionary<MonthKey, MonthlyTotal>();
            foreach (var entity in entities)
            {
                var key = new MonthKey(entity.WellId, entity.Year, entity.Month);
                result[key] = new MonthlyTotal
                {
                    WellId = entity.WellId,
                    Year = entity.Year,
                    Month = entity.Month,
                    OilVolSum = entity.OilVolSum,
                    GasVolSum = entity.GasVolSum,
                    WaterVolSum = entity.WaterVolSum,
                    WaterInjectionVolSum = entity.WaterInjectionVolSum,
                    OnStreamHrsSum = entity.OnStreamHrsSum,
                    DaysCount = entity.DaysCount
                };
            }

            return result;
        }

        /// <summary>Инкрементирует помесячные суммы в памяти.</summary>
        private static void ApplyDaily(
            Dictionary<MonthKey, MonthlyTotal> totals,
            HashSet<MonthKey> dirty,
            int wellId,
            DailyProductionData data)
        {
            var key = new MonthKey(wellId, data.Date.Year, data.Date.Month);
            if (!totals.TryGetValue(key, out var total))
            {
                total = new MonthlyTotal { WellId = wellId, Year = key.Year, Month = key.Month };
                totals[key] = total;
            }

            total.OilVolSum += data.OilVol ?? 0;
            total.GasVolSum += data.GasVol ?? 0;
            total.WaterVolSum += data.WaterVol ?? 0;
            total.WaterInjectionVolSum += data.WaterInjectionVol ?? 0;
            total.OnStreamHrsSum += data.OnStreamHrs ?? 0;
            total.DaysCount += 1;

            dirty.Add(key);
        }

        /// <summary>Записывает агрегаты через INSERT ... ON CONFLICT DO UPDATE (overwrite).</summary>
        private static async Task UpsertMonthlyAsync(
            CoreDbContext db,
            IReadOnlyList<MonthlyTotal> rows,
            CancellationToken cancellationToken)
        {
            const string sql =
                "INSERT INTO analytics.monthly_production " +
                "(well_id, year, month, oil_vol_sum, gas_vol_sum, water_vol_sum, water_injection_vol_sum, on_stream_hrs_sum, days_count) " +
                "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8) " +
                "ON CONFLICT (well_id, year, month) DO UPDATE SET " +
                "oil_vol_sum = EXCLUDED.oil_vol_sum, " +
                "gas_vol_sum = EXCLUDED.gas_vol_sum, " +
                "water_vol_sum = EXCLUDED.water_vol_sum, " +
                "water_injection_vol_sum = EXCLUDED.water_injection_vol_sum, " +
                "on_stream_hrs_sum = EXCLUDED.on_stream_hrs_sum, " +
                "days_count = EXCLUDED.days_count";

            foreach (var row in rows)
            {
                var parameters = new List<NpgsqlParameter>
                {
                    new NpgsqlParameter("@p0", NpgsqlDbType.Integer) { Value = row.WellId },
                    new NpgsqlParameter("@p1", NpgsqlDbType.Integer) { Value = row.Year },
                    new NpgsqlParameter("@p2", NpgsqlDbType.Integer) { Value = row.Month },
                    new NpgsqlParameter("@p3", NpgsqlDbType.Double) { Value = row.OilVolSum },
                    new NpgsqlParameter("@p4", NpgsqlDbType.Double) { Value = row.GasVolSum },
                    new NpgsqlParameter("@p5", NpgsqlDbType.Double) { Value = row.WaterVolSum },
                    new NpgsqlParameter("@p6", NpgsqlDbType.Double) { Value = row.WaterInjectionVolSum },
                    new NpgsqlParameter("@p7", NpgsqlDbType.Double) { Value = row.OnStreamHrsSum },
                    new NpgsqlParameter("@p8", NpgsqlDbType.Integer) { Value = row.DaysCount }
                };

                await db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
            }
        }

        /// <summary>Загружает соответствие npdWellBoreCode → well_id из таблицы wells.</summary>
        private static async Task<Dictionary<int, int>> LoadWellIdsAsync(
            CoreDbContext db,
            CancellationToken cancellationToken)
        {
            var wells = await db.Wells
                .AsNoTracking()
                .Select(w => new { w.Id, w.NpdWellBoreCode })
                .ToListAsync(cancellationToken);

            return wells.ToDictionary(w => w.NpdWellBoreCode, w => w.Id);
        }

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

        private sealed record Classification(MessageKind Kind, DailyProductionData? Daily, string? Error);

        /// <summary>Разбирает сообщение: тип/версия схемы и данные production.daily.</summary>
        private static Classification Classify(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new Classification(MessageKind.Invalid, null, "empty.message");
            }

            EventHeader header;
            try
            {
                header = JsonSerializer.Deserialize<EventHeader>(raw, MessagingJson.Options) ?? new EventHeader();
            }
            catch (JsonException)
            {
                return new Classification(MessageKind.Invalid, null, "unparseable.json");
            }

            if (header.SchemaVersion != EventTypes.SchemaVersion)
            {
                return new Classification(MessageKind.Invalid, null, "unknown.schemaVersion");
            }

            switch (header.EventType)
            {
                case EventTypes.WellRegistered:
                    return new Classification(MessageKind.Well, null, null);
                case EventTypes.ProductionDaily:
                {
                    try
                    {
                        var evt = JsonSerializer.Deserialize<DailyProductionEvent>(raw, MessagingJson.Options);
                        return evt?.Data is null
                            ? new Classification(MessageKind.Invalid, null, "missing.data")
                            : new Classification(MessageKind.Daily, evt.Data, null);
                    }
                    catch (JsonException)
                    {
                        return new Classification(MessageKind.Invalid, null, "unparseable.daily.event");
                    }
                }
                default:
                    return new Classification(MessageKind.Invalid, null, $"unknown.eventType:{header.EventType}");
            }
        }

        /// <summary>Счётчики обработки за один прогон агрегатора.</summary>
        private sealed class Counters
        {
            public long Received;
            public long Processed;
            public long ToDlq;
            public long Flushed;
        }

        /// <summary>Помесячный агрегат в памяти (полное значение, включая базовую линию).</summary>
        private sealed class MonthlyTotal
        {
            public int WellId { get; set; }
            public int Year { get; set; }
            public int Month { get; set; }
            public double OilVolSum { get; set; }
            public double GasVolSum { get; set; }
            public double WaterVolSum { get; set; }
            public double WaterInjectionVolSum { get; set; }
            public double OnStreamHrsSum { get; set; }
            public int DaysCount { get; set; }
        }

        private readonly record struct MonthKey(int WellId, int Year, int Month);

        private readonly record struct DayKey(int WellId, DateOnly Date);
    }
}

