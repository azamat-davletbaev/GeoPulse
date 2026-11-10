using System.Text;
using Confluent.Kafka;
using GeoPulse.Core.Events;
using GeoPulse.Core.Geometry;
using GeoPulse.Core.Models;
using GeoPulse.Core.Repositories;
using GeoPulse.Infrastructure;
using GeoPulse.Messaging;
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

        /// <summary>Пауза перед повтором после ошибки чтения из Kafka (топик не создан, брокер недоступен).</summary>
        private const int ConsumeErrorDelayMs = 1_000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProductionConsumerService> _logger;
        private readonly DlqPublisher _dlq;
        private readonly IWellIdResolver _wellIds;
        private readonly TopicProvisioner _topics;

        /// <summary>Конструктор.</summary>
        public ProductionConsumerService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ProductionConsumerService> logger,
            DlqPublisher dlq,
            IWellIdResolver wellIds,
            TopicProvisioner topics)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _dlq = dlq;
            _wellIds = wellIds;
            _topics = topics;
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

            // Топик создаём явно: подписка на несуществующий топик даёт UNKNOWN_TOPIC_OR_PARTITION,
            // а Kafka не создаёт топик по запросу консьюмера (KIP-361).
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
                // Фолбэк для брокеров, где автосоздание по подписке действительно работает.
                // Основной путь — явное создание топика через TopicProvisioner (см. ADR-015):
                // на Kafka 3+/4 c KIP-361 подписка консьюмера топик не создаёт.
                AllowAutoCreateTopics = true
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
                    ConsumeResult<string, string>? result;
                    try
                    {
                        result = consumer.Consume(TimeSpan.FromMilliseconds(250));
                    }
                    catch (ConsumeException ex)
                    {
                        // Топик может быть ещё не создан или брокер кратко недоступен: консьюмер
                        // не должен ронять хост (BackgroundServiceExceptionBehavior=StopHost),
                        // поэтому ждём и пробуем снова. Такие ошибки в DLQ не отправляются.
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
                        // Топик опустел — сбрасываем накопленный батч, чтобы остаток не зависел от размера батча.
                        await FlushDailyBatchAsync(db, batch, consumer, counters);
                        continue;
                    }

                    counters.Received++;

                    var classification = EventClassifier.Classify(result.Message.Value);

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
                                var wellId = await _wellIds.ResolveAsync(classification.Daily!.NpdWellBoreCode, stoppingToken);
                                if (wellId is null)
                                {
                                    await _dlq.PublishAsync(dlqTopic, result, DlqReasons.WellNotFound, stoppingToken);
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

                                var wellId = await _wellIds.ResolveAsync(
                                    classification.Trajectory!.NpdWellBoreCode, stoppingToken);
                                if (wellId is null)
                                {
                                    await _dlq.PublishAsync(dlqTopic, result, DlqReasons.WellNotFound, stoppingToken);
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
                await FlushDailyBatchAsync(db, batch, consumer, counters);

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

            await UpsertWellLocationAsync(db, wellId, trajectory);

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

        /// <summary>
        /// Апсертит координаты устья (well_locations) в WGS84: устьевые координаты из заголовка
        /// WITSML (Surface EW/NS, ED50 / UTM 31N) конвертируются в WGS84 через
        /// CoordinateTransform.Ed50Utm31ToWgs84 (EPSG:1613) — тем же преобразованием, что и сид.
        /// </summary>
        private static async Task UpsertWellLocationAsync(CoreDbContext db, int wellId, TrajectoryData trajectory)
        {
            if (!trajectory.SurfaceEasting.HasValue || !trajectory.SurfaceNorthing.HasValue)
            {
                return;
            }

            var (latitude, longitude) = CoordinateTransform.Ed50Utm31ToWgs84(
                trajectory.SurfaceEasting.Value, trajectory.SurfaceNorthing.Value);

            const string sql =
                "INSERT INTO well_locations (well_id, latitude, longitude, source) VALUES (@p0, @p1, @p2, @p3) " +
                "ON CONFLICT (well_id) DO UPDATE SET latitude = EXCLUDED.latitude, longitude = EXCLUDED.longitude, source = EXCLUDED.source";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                new NpgsqlParameter("@p0", NpgsqlDbType.Integer) { Value = wellId },
                new NpgsqlParameter("@p1", NpgsqlDbType.Double) { Value = latitude },
                new NpgsqlParameter("@p2", NpgsqlDbType.Double) { Value = longitude },
                new NpgsqlParameter("@p3", NpgsqlDbType.Text)
                {
                    Value = "well.trajectory Surface EW/NS (ED50 UTM31N -> WGS84 EPSG:1613)"
                });
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
