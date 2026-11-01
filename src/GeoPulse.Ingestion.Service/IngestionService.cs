using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using GeoPulse.Core.Models;
using GeoPulse.Core.Repositories;
using GeoPulse.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace GeoPulse.Ingestion.Service
{
    /// <summary>
    /// Парсер Excel-файла добычи Volve: читает лист Daily Production Data,
    /// пропускает строки с пустыми ключами и вставляет данные в PostgreSQL пакетами
    /// через upsert (INSERT ... ON CONFLICT (well_id, date) DO NOTHING).
    /// </summary>
    public sealed class IngestionService
    {
        private const string DailySheetName = "Daily Production Data";

        private readonly CoreDbContext _db;
        private readonly IWellRepository _wellRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IngestionService> _logger;

        /// <summary>Конструктор.</summary>
        public IngestionService(
            CoreDbContext db,
            IWellRepository wellRepository,
            IConfiguration configuration,
            ILogger<IngestionService> logger)
        {
            _db = db;
            _wellRepository = wellRepository;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>Запускает импорт.</summary>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            await _db.Database.MigrateAsync(cancellationToken);

            var configuredWorkbook = _configuration["Volve:ProductionWorkbook"];
            if (string.IsNullOrWhiteSpace(configuredWorkbook))
            {
                throw new InvalidOperationException(
                    "Не задан Volve:ProductionWorkbook в appsettings.json.");
            }

            // Относительный путь разрешается относительно рабочей директории
            // (сервис запускается из корня репозитория).
            var workbookPath = Path.GetFullPath(configuredWorkbook);
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

            // 3. Вставка upsert-ом: конфликты по (well_id, date) отбрасываются на стороне PostgreSQL.
            var batch = new List<DailyProduction>(batchSize);
            var rowsInserted = 0;
            var rowsSkippedDuplicate = 0;

            foreach (var parsed in parsedRows)
            {
                parsed.Production.WellId = npdToWellId[parsed.NpdWellBoreCode];
                batch.Add(parsed.Production);

                if (batch.Count >= batchSize)
                {
                    var inserted = await InsertBatchOnConflictDoNothingAsync(batch, cancellationToken);
                    rowsInserted += inserted;
                    rowsSkippedDuplicate += batch.Count - inserted;
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                var inserted = await InsertBatchOnConflictDoNothingAsync(batch, cancellationToken);
                rowsInserted += inserted;
                rowsSkippedDuplicate += batch.Count - inserted;
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

        /// <summary>Колонки <c>daily_production</c> в порядке параметров upsert-запроса.</summary>
        private static readonly (string Column, NpgsqlDbType Type, Func<DailyProduction, object?> Value)[] UpsertColumns =
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

        /// <summary>
        /// Вставляет пакет строк одним запросом
        /// <c>INSERT ... ON CONFLICT (well_id, date) DO NOTHING</c>.
        /// Возвращает число реально вставленных строк; остальные — конфликты по (well_id, date).
        /// </summary>
        private async Task<int> InsertBatchOnConflictDoNothingAsync(
            IReadOnlyList<DailyProduction> batch,
            CancellationToken cancellationToken)
        {
            var sql = new StringBuilder(batch.Count * 140 + 256);
            sql.Append("INSERT INTO daily_production (");
            sql.Append(string.Join(", ", UpsertColumns.Select(column => column.Column)));
            sql.Append(") VALUES ");

            var parameters = new List<NpgsqlParameter>(batch.Count * UpsertColumns.Length);

            for (var row = 0; row < batch.Count; row++)
            {
                if (row > 0)
                {
                    sql.Append(',');
                }

                sql.Append('(');
                for (var column = 0; column < UpsertColumns.Length; column++)
                {
                    if (column > 0)
                    {
                        sql.Append(',');
                    }

                    var name = $"@p{parameters.Count}";
                    sql.Append(name);
                    parameters.Add(new NpgsqlParameter(name, UpsertColumns[column].Type)
                    {
                        Value = UpsertColumns[column].Value(batch[row]) ?? DBNull.Value
                    });
                }

                sql.Append(')');
            }

            sql.Append(" ON CONFLICT (well_id, date) DO NOTHING");

            return await _db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, cancellationToken);
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
