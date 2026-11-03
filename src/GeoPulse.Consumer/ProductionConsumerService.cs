using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using GeoPulse.Core.Events;
using GeoPulse.Core.Models;
using GeoPulse.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace GeoPulse.Consumer
{
    /// <summary>
    /// Консьюмер топика production-daily: применяет миграции EF Core, апсертит скважины
    /// (well.registered) и батчами пишет суточную добычу (production.daily) через raw SQL
    /// с ON CONFLICT (well_id, date) DO NOTHING. Неразбираемые и неизвестные события
    /// перекладываются в DLQ-топик без потери исходного сообщения.
    /// </summary>
    public sealed class ProductionConsumerService : BackgroundService
    {
        private const string ConsumerGroup = "geopulse-consumer";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProductionConsumerService> _logger;
        private readonly IProducer<string, string> _dlqProducer;

        /// <summary>Конструктор.</summary>
        public ProductionConsumerService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ProductionConsumerService> logger,
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
            var batchSize = _configuration.GetValue("Kafka:BatchSize", 500);
            if (batchSize <= 0)
            {
                batchSize = 500;
            }

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = ConsumerGroup,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            consumer.Subscribe(topic);

            var batch = new List<DailyProduction>();

            var counters = new Counters();

            _logger.LogInformation("Консьюмер запущен: топик {Topic}, группа {Group}", topic, ConsumerGroup);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = consumer.Consume(TimeSpan.FromMilliseconds(250));
                    if (result is null)
                    {
                        // Топик опустел — сбрасываем накопленный батч, чтобы остаток не зависел от размера батча.
                        await FlushDailyBatchAsync(db, batch, consumer, counters);
                        continue;
                    }

                    counters.Received++;

                    var classification = Classify(result.Message.Value);

                    switch (classification.Kind)
                    {
                        case MessageKind.Well:
                        {
                            await FlushDailyBatchAsync(db, batch, consumer, counters);

                            await UpsertWellAsync(db, classification.Well!);
                            counters.Wells++;
                            consumer.Commit(result);
                            break;
                        }
                        case MessageKind.Daily:
                        {
                            var wellId = await ResolveWellIdAsync(db, classification.Daily!.NpdWellBoreCode);
                            if (wellId is null)
                            {
                                await PublishDlqAsync(dlqTopic, result, "well.not.found");
                                counters.ToDlq++;
                                consumer.Commit(result);
                                break;
                            }

                            batch.Add(ToDailyProduction(wellId.Value, classification.Daily));

                            if (batch.Count >= batchSize)
                            {
                                await FlushDailyBatchAsync(db, batch, consumer, counters);
                            }
                            break;
                        }
                        case MessageKind.Trajectory:
                        {
                            await FlushDailyBatchAsync(db, batch, consumer, counters);

                            var wellId = await ResolveWellIdAsync(db, classification.Trajectory!.NpdWellBoreCode);
                            if (wellId is null)
                            {
                                await PublishDlqAsync(dlqTopic, result, "well.not.found");
                                counters.ToDlq++;
                                consumer.Commit(result);
                                break;
                            }

                            await UpsertTrajectoryAsync(db, wellId.Value, classification.Trajectory);
                            counters.Trajectories++;
                            consumer.Commit(result);
                            break;
                        }
                        default:
                        {
                            await FlushDailyBatchAsync(db, batch, consumer, counters);

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
                await FlushDailyBatchAsync(db, batch, consumer, counters);

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
                    "Консьюмер завершён: получено {Received}, вставлено {Inserted}, пропущено (дубликаты) {Skipped}, в DLQ {Dlq}, скважин {Wells}, траекторий {Trajectories}",
                    counters.Received, counters.Inserted, counters.Skipped, counters.ToDlq, counters.Wells, counters.Trajectories);
            }
        }

        /// <summary>Сбрасывает батч добычи одним INSERT-ом и коммитит офсеты всех партиций.</summary>
        private async Task FlushDailyBatchAsync(
            CoreDbContext db,
            List<DailyProduction> batch,
            IConsumer<string, string> consumer,
            Counters counters)
        {
            if (batch.Count == 0)
            {
                return;
            }

            var written = await InsertDailyBatchAsync(db, batch);
            counters.Inserted += written;
            counters.Skipped += batch.Count - written;
            batch.Clear();

            // К этому моменту всё потреблённое уже персистентно записано — коммитим весь assignment.
            consumer.Commit();

            _logger.LogInformation(
                "Батч добычи обработан: получено {Received}, вставлено {Inserted}, пропущено (дубликаты) {Skipped}",
                counters.Received, counters.Inserted, counters.Skipped);
        }

        /// <summary>Счётчики обработки за один прогон консьюмера.</summary>
        private sealed class Counters
        {
            public long Received;
            public long Wells;
            public long Trajectories;
            public long Inserted;
            public long Skipped;
            public long ToDlq;
        }

        /// <summary>Апсерт скважины по уникальному индексу npd_well_bore_code.</summary>
        private static async Task UpsertWellAsync(CoreDbContext db, WellRegisteredData well)
        {
            const string sql = "INSERT INTO wells (npd_well_bore_code, name, field, type) VALUES (@p0, @p1, @p2, @p3) ON CONFLICT (npd_well_bore_code) DO UPDATE SET name = EXCLUDED.name, field = EXCLUDED.field, type = EXCLUDED.type";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                new NpgsqlParameter("@p0", NpgsqlDbType.Integer) { Value = well.NpdWellBoreCode },
                new NpgsqlParameter("@p1", NpgsqlDbType.Text) { Value = well.Name },
                new NpgsqlParameter("@p2", NpgsqlDbType.Text) { Value = well.Field },
                new NpgsqlParameter("@p3", NpgsqlDbType.Text) { Value = well.Type });
        }

        /// <summary>Ищет скважину по натуральному ключу; null — если скважина ещё не зарегистрирована.</summary>
        private static async Task<int?> ResolveWellIdAsync(CoreDbContext db, int npdWellBoreCode)
        {
            var well = await db.Wells
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.NpdWellBoreCode == npdWellBoreCode);

            return well?.Id;
        }

        /// <summary>Пишет траекторию: метаданные забоя (1:1, last wins) и замеры (reject дубликатов по (well_id, md)).</summary>
        private static async Task UpsertTrajectoryAsync(CoreDbContext db, int wellId, TrajectoryData trajectory)
        {
            const string metaSql =
                "INSERT INTO well_trajectory_meta (well_id, td_md, td_tvd) VALUES (@p0, @p1, @p2) " +
                "ON CONFLICT (well_id) DO UPDATE SET td_md = EXCLUDED.td_md, td_tvd = EXCLUDED.td_tvd";

            await db.Database.ExecuteSqlRawAsync(
                metaSql,
                new NpgsqlParameter("@p0", NpgsqlDbType.Integer) { Value = wellId },
                new NpgsqlParameter("@p1", NpgsqlDbType.Double) { Value = (object?)trajectory.TdMd ?? DBNull.Value },
                new NpgsqlParameter("@p2", NpgsqlDbType.Double) { Value = (object?)trajectory.TdTvd ?? DBNull.Value });

            if (trajectory.Points.Count == 0)
            {
                return;
            }

            var sql = new StringBuilder(trajectory.Points.Count * 90 + 160);
            sql.Append("INSERT INTO well_trajectories (well_id, md, tvd, inclination, azimuth, easting, northing) VALUES ");

            var parameters = new List<NpgsqlParameter>(trajectory.Points.Count * 7);
            for (var row = 0; row < trajectory.Points.Count; row++)
            {
                if (row > 0)
                {
                    sql.Append(',');
                }

                var point = trajectory.Points[row];
                sql.Append('(');
                for (var column = 0; column < TrajectoryColumns.Length; column++)
                {
                    if (column > 0)
                    {
                        sql.Append(',');
                    }

                    var name = $"@t{parameters.Count}";
                    sql.Append(name);
                    parameters.Add(new NpgsqlParameter(name, TrajectoryColumns[column].Type)
                    {
                        Value = TrajectoryColumns[column].Value(wellId, point)
                    });
                }

                sql.Append(')');
            }

            sql.Append(" ON CONFLICT (well_id, md) DO NOTHING");

            await db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters);
        }

        /// <summary>Колонки well_trajectories в порядке параметров upsert-запроса.</summary>
        private static readonly (string Column, NpgsqlDbType Type, Func<int, TrajectoryPointData, object> Value)[] TrajectoryColumns =
        {
            ("well_id", NpgsqlDbType.Integer, (wellId, _) => wellId),
            ("md", NpgsqlDbType.Double, (_, point) => point.Md),
            ("tvd", NpgsqlDbType.Double, (_, point) => point.Tvd),
            ("inclination", NpgsqlDbType.Double, (_, point) => point.Inclination),
            ("azimuth", NpgsqlDbType.Double, (_, point) => point.Azimuth),
            ("easting", NpgsqlDbType.Double, (_, point) => point.Easting),
            ("northing", NpgsqlDbType.Double, (_, point) => point.Northing)
        };

        /// <summary>Вставляет батч добычи одним INSERT ... ON CONFLICT DO NOTHING; возвращает число вставленных строк.</summary>
        private static async Task<int> InsertDailyBatchAsync(CoreDbContext db, IReadOnlyList<DailyProduction> batch)
        {
            var sql = new StringBuilder(batch.Count * 140 + 256);
            sql.Append("INSERT INTO daily_production (");
            sql.Append(string.Join(", ", DailyColumns.Select(column => column.Column)));
            sql.Append(") VALUES ");

            var parameters = new List<NpgsqlParameter>(batch.Count * DailyColumns.Length);

            for (var row = 0; row < batch.Count; row++)
            {
                if (row > 0)
                {
                    sql.Append(',');
                }

                sql.Append('(');
                for (var column = 0; column < DailyColumns.Length; column++)
                {
                    if (column > 0)
                    {
                        sql.Append(',');
                    }

                    var name = $"@p{parameters.Count}";
                    sql.Append(name);
                    parameters.Add(new NpgsqlParameter(name, DailyColumns[column].Type)
                    {
                        Value = DailyColumns[column].Value(batch[row]) ?? DBNull.Value
                    });
                }

                sql.Append(')');
            }

            sql.Append(" ON CONFLICT (well_id, date) DO NOTHING");

            return await db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters);
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

                var rawBody = result.Message.Value ?? string.Empty;
                var bodyPreview = rawBody.Length <= 100 ? rawBody : rawBody[..100];
                _logger.LogInformation(
                    "DLQ: eventType={EventType}, reason={Reason}, key={Key}, body={Body}",
                    GetEventType(rawBody),
                    reason,
                    result.Message.Key,
                    bodyPreview);
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Ошибка отправки в DLQ-топик {Topic}", dlqTopic);
            }
        }

        /// <summary>Извлекает eventType из тела сообщения; unknown — если тип не определён.</summary>
        private static string GetEventType(string raw)
        {
            try
            {
                var header = JsonSerializer.Deserialize<EventHeader>(raw, MessagingJson.Options);
                return string.IsNullOrWhiteSpace(header?.EventType) ? "unknown" : header.EventType;
            }
            catch (JsonException)
            {
                return "unparseable";
            }
        }

        /// <summary>Собирает доменную модель добычи из события, подставляя разрешённый wellId.</summary>
        private static DailyProduction ToDailyProduction(int wellId, DailyProductionData data) => new()
        {
            Id = Guid.NewGuid(),
            WellId = wellId,
            Date = data.Date,
            OnStreamHrs = data.OnStreamHrs,
            AvgDownholePressure = data.AvgDownholePressure,
            AvgDownholeTemperature = data.AvgDownholeTemperature,
            AvgDpTubing = data.AvgDpTubing,
            AvgAnnulusPressure = data.AvgAnnulusPressure,
            AvgChokeSize = data.AvgChokeSize,
            AvgWhp = data.AvgWhp,
            AvgWht = data.AvgWht,
            DpChokeSize = data.DpChokeSize,
            OilVol = data.OilVol,
            GasVol = data.GasVol,
            WaterVol = data.WaterVol,
            WaterInjectionVol = data.WaterInjectionVol
        };

        private enum MessageKind
        {
            Well,
            Daily,
            Trajectory,
            Invalid
        }

        private sealed record Classification(
            MessageKind Kind,
            WellRegisteredData? Well,
            DailyProductionData? Daily,
            TrajectoryData? Trajectory,
            string? Error);

        /// <summary>Разбирает сообщение: определяет тип/версию схемы и десериализует данные.</summary>
        private static Classification Classify(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new Classification(MessageKind.Invalid, null, null, null, "empty.message");
            }

            EventHeader header;
            try
            {
                header = JsonSerializer.Deserialize<EventHeader>(raw, MessagingJson.Options) ?? new EventHeader();
            }
            catch (JsonException)
            {
                return new Classification(MessageKind.Invalid, null, null, null, "unparseable.json");
            }

            if (header.SchemaVersion != EventTypes.SchemaVersion)
            {
                return new Classification(MessageKind.Invalid, null, null, null, "unknown.schemaVersion");
            }

            switch (header.EventType)
            {
                case EventTypes.WellRegistered:
                {
                    try
                    {
                        var evt = JsonSerializer.Deserialize<WellRegisteredEvent>(raw, MessagingJson.Options);
                        return evt?.Data is null
                            ? new Classification(MessageKind.Invalid, null, null, null, "missing.data")
                            : new Classification(MessageKind.Well, evt.Data, null, null, null);
                    }
                    catch (JsonException)
                    {
                        return new Classification(MessageKind.Invalid, null, null, null, "unparseable.well.event");
                    }
                }
                case EventTypes.ProductionDaily:
                {
                    try
                    {
                        var evt = JsonSerializer.Deserialize<DailyProductionEvent>(raw, MessagingJson.Options);
                        return evt?.Data is null
                            ? new Classification(MessageKind.Invalid, null, null, null, "missing.data")
                            : new Classification(MessageKind.Daily, null, evt.Data, null, null);
                    }
                    catch (JsonException)
                    {
                        return new Classification(MessageKind.Invalid, null, null, null, "unparseable.daily.event");
                    }
                }
                case EventTypes.Trajectory:
                {
                    try
                    {
                        var evt = JsonSerializer.Deserialize<TrajectoryEvent>(raw, MessagingJson.Options);
                        return evt?.Data is null
                            ? new Classification(MessageKind.Invalid, null, null, null, "missing.data")
                            : new Classification(MessageKind.Trajectory, null, null, evt.Data, null);
                    }
                    catch (JsonException)
                    {
                        return new Classification(MessageKind.Invalid, null, null, null, "unparseable.trajectory.event");
                    }
                }
                default:
                    return new Classification(MessageKind.Invalid, null, null, null, $"unknown.eventType:{header.EventType}");
            }
        }

        /// <summary>Колонки daily_production в порядке параметров upsert-запроса.</summary>
        private static readonly (string Column, NpgsqlDbType Type, Func<DailyProduction, object?> Value)[] DailyColumns =
        {
            ("id", NpgsqlDbType.Uuid, item => item.Id),
            ("well_id", NpgsqlDbType.Integer, item => item.WellId),
            ("date", NpgsqlDbType.Date, item => item.Date),
            ("on_stream_hrs", NpgsqlDbType.Double, item => item.OnStreamHrs),
            ("avg_downhole_pressure", NpgsqlDbType.Double, item => item.AvgDownholePressure),
            ("avg_downhole_temperature", NpgsqlDbType.Double, item => item.AvgDownholeTemperature),
            ("avg_dp_tubing", NpgsqlDbType.Double, item => item.AvgDpTubing),
            ("avg_annulus_pressure", NpgsqlDbType.Double, item => item.AvgAnnulusPressure),
            ("avg_choke_size", NpgsqlDbType.Double, item => item.AvgChokeSize),
            ("avg_whp", NpgsqlDbType.Double, item => item.AvgWhp),
            ("avg_wht", NpgsqlDbType.Double, item => item.AvgWht),
            ("dp_choke_size", NpgsqlDbType.Double, item => item.DpChokeSize),
            ("oil_vol", NpgsqlDbType.Double, item => item.OilVol),
            ("gas_vol", NpgsqlDbType.Double, item => item.GasVol),
            ("water_vol", NpgsqlDbType.Double, item => item.WaterVol),
            ("water_injection_vol", NpgsqlDbType.Double, item => item.WaterInjectionVol)
        };
    }
}
