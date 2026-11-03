using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using ExcelDataReader;
using GeoPulse.Core.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GeoPulse.Ingestion.Service
{
    /// <summary>
    /// Парсер Excel-файла добычи Volve: читает лист Daily Production Data,
    /// пропускает строки с пустыми ключами и публикует события в Kafka
    /// (well.registered, затем production.daily).
    /// </summary>
    public sealed class IngestionService
    {
        private const string DailySheetName = "Daily Production Data";

        private readonly IConfiguration _configuration;
        private readonly ILogger<IngestionService> _logger;

        /// <summary>Конструктор.</summary>
        public IngestionService(IConfiguration configuration, ILogger<IngestionService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>Запускает импорт.</summary>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            var configuredWorkbook = _configuration["Volve:ProductionWorkbook"];
            if (string.IsNullOrWhiteSpace(configuredWorkbook))
            {
                throw new InvalidOperationException("Не задан Volve:ProductionWorkbook в appsettings.json.");
            }

            var wellboreRoot = _configuration["Volve:WellboreRoot"];

            var bootstrapServers = _configuration["Kafka:BootstrapServers"];
            if (string.IsNullOrWhiteSpace(bootstrapServers))
            {
                throw new InvalidOperationException("Не задан Kafka:BootstrapServers в appsettings.json.");
            }

            var topic = _configuration["Kafka:Topic"];
            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new InvalidOperationException("Не задан Kafka:Topic в appsettings.json.");
            }

            // Относительный путь разрешается относительно рабочей директории
            // (сервис запускается из корня репозитория).
            var workbookPath = Path.GetFullPath(configuredWorkbook);
            if (!File.Exists(workbookPath))
            {
                _logger.LogError("Excel-файл не найден: {Path}", workbookPath);
                return;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            using var stream = File.Open(workbookPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
            });

            var table = dataSet.Tables[DailySheetName];
            if (table is null)
            {
                _logger.LogError("Лист '{Sheet}' не найден. Доступны: {Sheets}",
                    DailySheetName,
                    string.Join(", ", dataSet.Tables.Cast<DataTable>().Select(t => t.TableName)));
                return;
            }

            var wellsByNpd = new Dictionary<int, WellRegisteredData>();
            var typeCounts = new Dictionary<int, Dictionary<string, int>>();
            var dailyRows = new List<ParsedDailyRow>();

            var rowsRead = 0;
            var rowsSkippedNullKey = 0;

            foreach (DataRow row in table.Rows)
            {
                rowsRead++;

                var npdCode = ParseInt(Cell(row, "NPD_WELL_BORE_CODE"));
                var date = ParseDate(Cell(row, "DATEPRD"));
                if (npdCode is null || date is null)
                {
                    rowsSkippedNullKey++;
                    continue;
                }

                if (!wellsByNpd.TryGetValue(npdCode.Value, out var well))
                {
                    well = new WellRegisteredData
                    {
                        NpdWellBoreCode = npdCode.Value,
                        Name = Text(Cell(row, "NPD_WELL_BORE_NAME"))
                            ?? Text(Cell(row, "WELL_BORE_CODE"))
                            ?? npdCode.Value.ToString(CultureInfo.InvariantCulture),
                        Field = Text(Cell(row, "NPD_FIELD_NAME")) ?? string.Empty,
                        Type = string.Empty
                    };
                    wellsByNpd[npdCode.Value] = well;
                }

                var wellType = Text(Cell(row, "WELL_TYPE")) ?? string.Empty;
                if (!typeCounts.TryGetValue(npdCode.Value, out var counts))
                {
                    counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    typeCounts[npdCode.Value] = counts;
                }

                counts[wellType] = counts.TryGetValue(wellType, out var existing) ? existing + 1 : 1;

                dailyRows.Add(new ParsedDailyRow(new DailyProductionData
                {
                    NpdWellBoreCode = npdCode.Value,
                    Date = date.Value,
                    OnStreamHrs = ParseDouble(Cell(row, "ON_STREAM_HRS")),
                    AvgDownholePressure = ParseDouble(Cell(row, "AVG_DOWNHOLE_PRESSURE")),
                    AvgDownholeTemperature = ParseDouble(Cell(row, "AVG_DOWNHOLE_TEMPERATURE")),
                    AvgDpTubing = ParseDouble(Cell(row, "AVG_DP_TUBING")),
                    AvgAnnulusPressure = ParseDouble(Cell(row, "AVG_ANNULUS_PRESS")),
                    AvgChokeSize = ParseDouble(Cell(row, "AVG_CHOKE_SIZE_P")),
                    AvgWhp = ParseDouble(Cell(row, "AVG_WHP_P")),
                    AvgWht = ParseDouble(Cell(row, "AVG_WHT_P")),
                    DpChokeSize = ParseDouble(Cell(row, "DP_CHOKE_SIZE")),
                    OilVol = ParseDouble(Cell(row, "BORE_OIL_VOL")),
                    GasVol = ParseDouble(Cell(row, "BORE_GAS_VOL")),
                    WaterVol = ParseDouble(Cell(row, "BORE_WAT_VOL")),
                    WaterInjectionVol = ParseDouble(Cell(row, "BORE_WI_VOL"))
                }));
            }

            foreach (var (npd, well) in wellsByNpd)
            {
                if (typeCounts.TryGetValue(npd, out var counts) && counts.Count > 0)
                {
                    well.Type = counts.OrderByDescending(pair => pair.Value).First().Key;
                }
            }

            _logger.LogInformation("Прочитано строк: {Rows}, из них с пустым ключом пропущено: {Skipped}",
                rowsRead, rowsSkippedNullKey);

            var producerConfig = new ProducerConfig { BootstrapServers = bootstrapServers };
            using var producer = new ProducerBuilder<string, string>(producerConfig).Build();

            var wellEvents = 0;
            var trajectoryEvents = 0;
            var dailyEvents = 0;

            // 1. Скважины: сначала регистрируем все стволы, чтобы консьюмер мог их разрешить.
            foreach (var well in wellsByNpd.Values.OrderBy(w => w.NpdWellBoreCode))
            {
                Publish(producer, topic, well.NpdWellBoreCode, new WellRegisteredEvent { Data = well });
                wellEvents++;
            }

            // 2. Траектории: после регистрации скважин публикуем well.trajectory (один event на ствол).
            if (!string.IsNullOrWhiteSpace(wellboreRoot))
            {
                var trajectoryMap = BuildTrajectoryMap(wellboreRoot, wellsByNpd);
                foreach (var (npd, filePath) in trajectoryMap.OrderBy(pair => pair.Key))
                {
                    var trajectory = ReadTrajectory(filePath);
                    if (trajectory is null)
                    {
                        _logger.LogWarning("Траектория для скважины {Npd} не распознана: {Path}", npd, filePath);
                        continue;
                    }

                    Publish(producer, topic, npd, new TrajectoryEvent
                    {
                        Data = new TrajectoryData
                        {
                            NpdWellBoreCode = npd,
                            TdMd = trajectory.TdMd,
                            TdTvd = trajectory.TdTvd,
                            Points = trajectory.Points
                        }
                    });
                    trajectoryEvents++;
                }
            }

            // 3. Добыча: ключ = npdWellBoreCode — сохраняет порядок событий в пределах скважины.
            foreach (var row in dailyRows)
            {
                Publish(producer, topic, row.Data.NpdWellBoreCode, new DailyProductionEvent { Data = row.Data });
                dailyEvents++;
            }

            producer.Flush(TimeSpan.FromSeconds(30));

            _logger.LogInformation(
                "Публикация завершена: прочитано строк {Read}, пропущено (пустой ключ) {Skipped}, опубликовано well.registered {Wells}, well.trajectory {Trajectory}, production.daily {Daily}",
                rowsRead, rowsSkippedNullKey, wellEvents, trajectoryEvents, dailyEvents);

            Console.WriteLine();
            Console.WriteLine("=== ИТОГОВАЯ СТАТИСТИКА ===");
            Console.WriteLine($"Прочитано строк:              {rowsRead}");
            Console.WriteLine($"Пропущено (пустой ключ):      {rowsSkippedNullKey}");
            Console.WriteLine($"Опубликовано well.registered: {wellEvents}");
            Console.WriteLine($"Опубликовано well.trajectory: {trajectoryEvents}");
            Console.WriteLine($"Опубликовано production.daily: {dailyEvents}");
        }

        /// <summary>Сериализует и публикует событие в топик с ключом = NPD-код ствола.</summary>
        private void Publish(IProducer<string, string> producer, string topic, int npdWellBoreCode, object evt)
        {
            var key = npdWellBoreCode.ToString(CultureInfo.InvariantCulture);
            var value = JsonSerializer.Serialize(evt, MessagingJson.Options);

            producer.Produce(topic, new Message<string, string> { Key = key, Value = value },
                report =>
                {
                    if (report.Error.IsError)
                    {
                        _logger.LogError("Ошибка доставки в Kafka: {Reason}", report.Error.Reason);
                    }
                });
        }

        /// <summary>Сопоставляет коды НПД с файлами *_ACTUAL по имени каталога ствола (имя из Excel → «/» → «_»).</summary>
        private static Dictionary<int, string> BuildTrajectoryMap(
            string wellboreRoot,
            Dictionary<int, WellRegisteredData> wellsByNpd)
        {
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var well in wellsByNpd.Values)
            {
                byName[NormalizeWellName(well.Name)] = well.NpdWellBoreCode;
            }

            var map = new Dictionary<int, string>();
            if (!Directory.Exists(wellboreRoot))
            {
                return map;
            }

            foreach (var file in Directory.EnumerateFiles(wellboreRoot, "*_ACTUAL", SearchOption.AllDirectories))
            {
                var dirName = Path.GetFileName(Path.GetDirectoryName(file)) ?? string.Empty;
                if (byName.TryGetValue(dirName, out var npd) && !map.ContainsKey(npd))
                {
                    map[npd] = file;
                }
            }

            return map;
        }

        private static string NormalizeWellName(string name) => name.Replace("/", "_");

        private static ParsedTrajectory? ReadTrajectory(string filePath)
        {
            try
            {
                return TrajectoryParser.Parse(File.ReadAllText(filePath));
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static object? Cell(DataRow row, string column) =>
            row.Table.Columns.Contains(column) ? row[column] : null;

        private static string? Text(object? raw)
        {
            if (raw is null or DBNull)
            {
                return null;
            }

            var text = raw.ToString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static double? ParseDouble(object? raw)
        {
            if (raw is null or DBNull)
            {
                return null;
            }

            switch (raw)
            {
                case double d: return d;
                case float f: return f;
                case decimal m: return (double)m;
                case byte b: return b;
                case short s: return s;
                case int i: return i;
                case long l: return l;
            }

            var text = raw.ToString()?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static int? ParseInt(object? raw)
        {
            var value = ParseDouble(raw);
            return value.HasValue ? (int)Math.Round(value.Value, MidpointRounding.AwayFromZero) : null;
        }

        private static DateOnly? ParseDate(object? raw)
        {
            if (raw is null or DBNull)
            {
                return null;
            }

            if (raw is DateTime dateTime)
            {
                return DateOnly.FromDateTime(dateTime);
            }

            var text = raw.ToString()?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                && serial >= 1 && serial < 100000)
            {
                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            }

            return DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }

        private sealed record ParsedDailyRow(DailyProductionData Data);
    }
}
