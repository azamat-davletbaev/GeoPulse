using System.Data;
using System.Globalization;
using ExcelDataReader;
using GeoPulse.Core.Models;
using GeoPulse.Core.Repositories;
using GeoPulse.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GeoPulse.Ingestion.Service
{
    /// <summary>
    /// Парсер Excel-файла добычи Volve: читает лист Daily Production Data,
    /// пропускает строки с пустыми ключами, дедуплицирует по паре (WellId, Date)
    /// и пишет данные в PostgreSQL батчами.
    /// </summary>
    public sealed class IngestionService
    {
        private const string DailySheetName = "Daily Production Data";

        private readonly CoreDbContext _db;
        private readonly IWellRepository _wellRepository;
        private readonly IDailyProductionRepository _dailyProductionRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IngestionService> _logger;

        /// <summary>Конструктор.</summary>
        public IngestionService(
            CoreDbContext db,
            IWellRepository wellRepository,
            IDailyProductionRepository dailyProductionRepository,
            IConfiguration configuration,
            ILogger<IngestionService> logger)
        {
            _db = db;
            _wellRepository = wellRepository;
            _dailyProductionRepository = dailyProductionRepository;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>Запускает импорт.</summary>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            await _db.Database.MigrateAsync(cancellationToken);

            var workbookPath = _configuration["Volve:ProductionWorkbook"]
                ?? Path.Combine("xml", "Volve_Production_data", "Volve production data.xlsx");
            var batchSize = _configuration.GetValue("Volve:BatchSize", 500);
            if (batchSize <= 0)
            {
                batchSize = 500;
            }

            if (!File.Exists(workbookPath))
            {
                _logger.LogError("Excel-файл не найден: {Path}", workbookPath);
                return;
            }

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

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

            var wellsByNpd = new Dictionary<int, Well>();
            var typeCounts = new Dictionary<int, Dictionary<string, int>>();
            var parsedRows = new List<ParsedDailyRow>();

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
                    well = new Well
                    {
                        NpdWellBoreCode = npdCode.Value,
                        Name = Text(Cell(row, "NPD_WELL_BORE_NAME")) ?? Text(Cell(row, "WELL_BORE_CODE")) ?? npdCode.Value.ToString(),
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

                parsedRows.Add(new ParsedDailyRow(
                    npdCode.Value,
                    new DailyProduction
                    {
                        Id = Guid.NewGuid(),
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

            // 2. Скважины: загружаем существующие, добавляем новые.
            var existingWells = await _wellRepository.GetAllAsync(cancellationToken);
            var npdToWellId = existingWells.ToDictionary(well => well.NpdWellBoreCode, well => well.Id);

            var newWells = wellsByNpd.Values
                .Where(well => !npdToWellId.ContainsKey(well.NpdWellBoreCode))
                .ToList();
            if (newWells.Count > 0)
            {
                await _wellRepository.AddRangeAsync(newWells, cancellationToken);
                foreach (var well in newWells)
                {
                    npdToWellId[well.NpdWellBoreCode] = well.Id;
                }

                _logger.LogInformation("Добавлено скважин: {Wells}", newWells.Count);
            }

            // 3. Дедупликация: существующие пары (WellId, Date).
            var existingDaily = await _dailyProductionRepository.GetAllAsync(cancellationToken);
            var existingKeys = existingDaily
                .Select(item => (item.WellId, item.Date))
                .ToHashSet();

            var seenThisRun = new HashSet<(int WellId, DateOnly Date)>();

            var batch = new List<DailyProduction>(batchSize);
            var rowsInserted = 0;
            var rowsSkippedDuplicate = 0;

            foreach (var parsed in parsedRows)
            {
                var wellId = npdToWellId[parsed.NpdWellBoreCode];
                var key = (wellId, parsed.Production.Date);

                if (existingKeys.Contains(key) || !seenThisRun.Add(key))
                {
                    rowsSkippedDuplicate++;
                    continue;
                }

                parsed.Production.WellId = wellId;
                batch.Add(parsed.Production);

                if (batch.Count >= batchSize)
                {
                    await _dailyProductionRepository.AddRangeAsync(batch, cancellationToken);
                    rowsInserted += batch.Count;
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await _dailyProductionRepository.AddRangeAsync(batch, cancellationToken);
                rowsInserted += batch.Count;
            }

            var totalSkipped = rowsSkippedNullKey + rowsSkippedDuplicate;

            _logger.LogInformation(
                "Импорт завершён: прочитано {Read}, вставлено {Inserted}, пропущено {Skipped} (пустой ключ: {NullKey}, дубликаты: {Dup})",
                rowsRead, rowsInserted, totalSkipped, rowsSkippedNullKey, rowsSkippedDuplicate);

            Console.WriteLine();
            Console.WriteLine("=== ИТОГОВАЯ СТАТИСТИКА ===");
            Console.WriteLine($"Прочитано строк:     {rowsRead}");
            Console.WriteLine($"Вставлено строк:     {rowsInserted}");
            Console.WriteLine($"Пропущено всего:     {totalSkipped}");
            Console.WriteLine($"  - пустой ключ:     {rowsSkippedNullKey}");
            Console.WriteLine($"  - дубликаты:       {rowsSkippedDuplicate}");
            Console.WriteLine($"Скважин:             {npdToWellId.Count}");
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

        private sealed record ParsedDailyRow(int NpdWellBoreCode, DailyProduction Production);
    }
}
