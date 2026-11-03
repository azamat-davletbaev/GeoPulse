using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using GeoPulse.Core.Repositories;
using GeoPulse.WebAPI.Models;

namespace GeoPulse.WebAPI
{
    /// <summary>
    /// Формирует Excel/CSV-отчёты по добыче месторождения. Источник данных — ClickHouse
    /// (через <see cref="IMonthlyProductionReader"/>), справочник скважин — PostgreSQL
    /// (через <see cref="IWellRepository"/>). Обводнённость и доли считаются здесь один раз
    /// и переиспользуются и в XLSX, и в CSV; на фронте эти расчёты не дублируются.
    /// </summary>
    public sealed class ReportService
    {
        /// <summary>Единый числовой формат всех отчётов (`#,##0.00`).</summary>
        private const string NumberFormat = "#,##0.00";

        private readonly IWellRepository _wells;
        private readonly IMonthlyProductionReader _analytics;

        /// <summary>Конструктор.</summary>
        public ReportService(IWellRepository wells, IMonthlyProductionReader analytics)
        {
            _wells = wells;
            _analytics = analytics;
        }

        /// <summary>Загружает данные и считает все производные показатели (обводнённость, доли) один раз.</summary>
        public async Task<MonthlyReportData> LoadAsync(CancellationToken cancellationToken)
        {
            var wells = await _wells.GetAllAsync(cancellationToken);
            var monthly = await _analytics.GetAllMonthlyAsync(cancellationToken);
            var summary = await _analytics.GetSummaryAsync(cancellationToken);

            var totalOil = summary.TotalOilVol;

            var monthlyRows = monthly
                .GroupBy(item => (item.Year, item.Month))
                .OrderBy(group => group.Key.Year)
                .ThenBy(group => group.Key.Month)
                .Select(group =>
                {
                    var oil = group.Sum(item => item.OilVolSum);
                    var water = group.Sum(item => item.WaterVolSum);
                    return new MonthlyReportRow(group.Key.Year, group.Key.Month, oil, water, WaterCutPct(oil, water));
                })
                .ToList();

            var byWell = monthly
                .GroupBy(item => item.WellId)
                .ToDictionary(
                    group => group.Key,
                    group => (Oil: group.Sum(item => item.OilVolSum), Water: group.Sum(item => item.WaterVolSum)));

            var wellRows = wells
                .OrderBy(well => well.Id)
                .Select(well =>
                {
                    byWell.TryGetValue(well.Id, out var aggregate);
                    var oil = aggregate.Oil;
                    var water = aggregate.Water;
                    return new WellReportRow(well.Name, well.NpdWellBoreCode, oil, water, SharePct(oil, totalOil));
                })
                .ToList();

            return new MonthlyReportData(
                summary,
                BuildPeriod(monthly),
                wells.Count,
                monthlyRows,
                wellRows);
        }

        /// <summary>
        /// Загружает данные для отчёта по одной скважине: справочные поля — из PostgreSQL,
        /// помесячная добыча — из ClickHouse. Обводнённость и доля считаются здесь же, теми же
        /// функциями, что и в отчёте по месторождению. Возвращает null, если скважины нет.
        /// </summary>
        /// <param name="wellId">Внутренний идентификатор скважины (wells.Id).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public async Task<WellReportData?> LoadWellAsync(int wellId, CancellationToken cancellationToken)
        {
            var well = await _wells.GetByIdAsync(wellId, cancellationToken);
            if (well is null)
            {
                return null;
            }

            var monthly = await _analytics.GetAllMonthlyAsync(cancellationToken);
            var summary = await _analytics.GetSummaryAsync(cancellationToken);

            // Помесячные строки уже уникальны в разрезе (well_id, year, month), поэтому
            // группировка не нужна — достаточно отфильтровать и упорядочить.
            var rows = monthly
                .Where(item => item.WellId == wellId)
                .OrderBy(item => item.Year)
                .ThenBy(item => item.Month)
                .ToList();

            var oil = rows.Sum(item => item.OilVolSum);
            var water = rows.Sum(item => item.WaterVolSum);

            var monthlyRows = rows
                .Select(item => new MonthlyReportRow(
                    item.Year,
                    item.Month,
                    item.OilVolSum,
                    item.WaterVolSum,
                    WaterCutPct(item.OilVolSum, item.WaterVolSum)))
                .ToList();

            return new WellReportData(
                well.Name,
                well.Field,
                oil,
                water,
                BuildPeriod(rows),
                SharePct(oil, summary.TotalOilVol),
                monthlyRows);
        }

        /// <summary>
        /// Генерация XLSX-файла. Сознательно без ArrayPool и без параллелизации расчётов: текущий
        /// объём — сотни строк (526 агрегатов), выгрузка укладывается в единицы миллисекунд.
        /// Порог оправданности оптимизаций — порядка 10^5 строк и выше, когда аллокации буферов
        /// начинают заметно влиять на GC; до этого спекулятивная оптимизация только усложняет код.
        /// Генерация синхронна по своей природе (у ClosedXML нет async-API), но она CPU-bound и быстра.
        /// </summary>
        public byte[] BuildXlsx(MonthlyReportData data)
        {
            using var workbook = new XLWorkbook();
            BuildSummarySheet(workbook, data);
            BuildMonthlySheet(workbook, "Помесячно", data.Monthly);
            BuildWellsSheet(workbook, data);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Генерация XLSX-отчёта по одной скважине: листы «Скважина» (справочные поля и итоги)
        /// и «Помесячно». Лист «Помесячно» и стили строятся теми же функциями, что и в отчёте
        /// по месторождению, — формат ячеек и шапки совпадает.
        /// </summary>
        /// <param name="data">Подготовленные данные скважины.</param>
        public byte[] BuildWellXlsx(WellReportData data)
        {
            using var workbook = new XLWorkbook();
            BuildWellSheet(workbook, data);
            BuildMonthlySheet(workbook, "Помесячно", data.Monthly);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        /// <summary>Генерация CSV (запятая, BOM, заголовки на английском) без внешних библиотек.</summary>
        public byte[] BuildCsv(MonthlyReportData data)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Year,Month,OilVol,WaterVol,WaterCutPct");
            foreach (var row in data.Monthly)
            {
                builder.AppendLine(
                    $"{row.Year},{row.Month},{Format(row.OilVol)},{Format(row.WaterVol)},{Format(row.WaterCutPct)}");
            }

            var preamble = Encoding.UTF8.GetPreamble();
            var body = Encoding.UTF8.GetBytes(builder.ToString());
            var result = new byte[preamble.Length + body.Length];
            preamble.CopyTo(result, 0);
            body.CopyTo(result, preamble.Length);
            return result;
        }

        private static void BuildSummarySheet(XLWorkbook workbook, MonthlyReportData data)
        {
            var sheet = workbook.Worksheets.Add("Сводка");
            sheet.Cell(1, 1).Value = "Показатель";
            sheet.Cell(1, 2).Value = "Значение";
            sheet.Cell(2, 1).Value = "Суммарная нефть, м³";
            sheet.Cell(2, 2).Value = data.Summary.TotalOilVol;
            sheet.Cell(3, 1).Value = "Суммарная вода, м³";
            sheet.Cell(3, 2).Value = data.Summary.TotalWaterVol;
            sheet.Cell(4, 1).Value = "Период";
            sheet.Cell(4, 2).Value = data.Period;
            sheet.Cell(5, 1).Value = "Число скважин";
            sheet.Cell(5, 2).Value = data.WellCount;

            StyleHeader(sheet.Range(1, 1, 1, 2));
            StyleNumbers(sheet.Cell(2, 2).AsRange());
            StyleNumbers(sheet.Cell(3, 2).AsRange());
            sheet.Column(1).AdjustToContents();
            sheet.Column(2).AdjustToContents();
        }

        private static void BuildWellSheet(XLWorkbook workbook, WellReportData data)
        {
            var sheet = workbook.Worksheets.Add("Скважина");
            sheet.Cell(1, 1).Value = "Показатель";
            sheet.Cell(1, 2).Value = "Значение";
            sheet.Cell(2, 1).Value = "Скважина";
            sheet.Cell(2, 2).Value = data.Name;
            sheet.Cell(3, 1).Value = "Месторождение";
            sheet.Cell(3, 2).Value = data.Field;
            sheet.Cell(4, 1).Value = "Суммарная нефть, м³";
            sheet.Cell(4, 2).Value = data.TotalOilVol;
            sheet.Cell(5, 1).Value = "Суммарная вода, м³";
            sheet.Cell(5, 2).Value = data.TotalWaterVol;
            sheet.Cell(6, 1).Value = "Период";
            sheet.Cell(6, 2).Value = data.Period;
            sheet.Cell(7, 1).Value = "Доля от добычи месторождения, %";
            sheet.Cell(7, 2).Value = data.SharePct;

            StyleHeader(sheet.Range(1, 1, 1, 2));
            StyleNumbers(sheet.Range(4, 2, 5, 2));
            StyleNumbers(sheet.Cell(7, 2).AsRange());
            sheet.Column(1).AdjustToContents();
            sheet.Column(2).AdjustToContents();
        }

        private static void BuildMonthlySheet(
            XLWorkbook workbook,
            string sheetName,
            IReadOnlyList<MonthlyReportRow> monthly)
        {
            var sheet = workbook.Worksheets.Add(sheetName);
            sheet.Cell(1, 1).Value = "Год";
            sheet.Cell(1, 2).Value = "Месяц";
            sheet.Cell(1, 3).Value = "Нефть, м³";
            sheet.Cell(1, 4).Value = "Вода, м³";
            sheet.Cell(1, 5).Value = "Обводнённость, %";

            var row = 2;
            foreach (var item in monthly)
            {
                sheet.Cell(row, 1).Value = item.Year;
                sheet.Cell(row, 2).Value = item.Month;
                sheet.Cell(row, 3).Value = item.OilVol;
                sheet.Cell(row, 4).Value = item.WaterVol;
                sheet.Cell(row, 5).Value = item.WaterCutPct;
                row++;
            }

            StyleHeader(sheet.Range(1, 1, 1, 5));
            if (row > 2)
            {
                StyleNumbers(sheet.Range(2, 3, row - 1, 5));
            }

            sheet.Columns().AdjustToContents();
        }

        private static void BuildWellsSheet(XLWorkbook workbook, MonthlyReportData data)
        {
            var sheet = workbook.Worksheets.Add("По скважинам");
            sheet.Cell(1, 1).Value = "Скважина";
            sheet.Cell(1, 2).Value = "Нефть, м³";
            sheet.Cell(1, 3).Value = "Вода, м³";
            sheet.Cell(1, 4).Value = "Доля, %";

            var row = 2;
            foreach (var item in data.Wells)
            {
                sheet.Cell(row, 1).Value = item.Name;
                sheet.Cell(row, 2).Value = item.OilVol;
                sheet.Cell(row, 3).Value = item.WaterVol;
                sheet.Cell(row, 4).Value = item.SharePct;
                row++;
            }

            sheet.Range(1, 1, 1, 4).Style.Font.Bold = true;
            sheet.Range(2, 2, row - 1, 4).Style.NumberFormat.Format = "#,##0.00";
            sheet.Columns().AdjustToContents();
        }

        /// <summary>Общая шапка всех листов: жирная строка заголовков.</summary>
        private static void StyleHeader(IXLRange header)
        {
            header.Style.Font.Bold = true;
        }

        /// <summary>Общий числовой формат всех отчётов — один и тот же `#,##0.00`.</summary>
        private static void StyleNumbers(IXLRange range)
        {
            range.Style.NumberFormat.Format = NumberFormat;
        }

        private static string BuildPeriod(IReadOnlyList<MonthlyProductionFlatDto> monthly)
        {
            if (monthly.Count == 0)
            {
                return "—";
            }

            var first = monthly[0];
            var last = monthly[^1];
            return $"{first.Year}-{first.Month:00} — {last.Year}-{last.Month:00}";
        }

        private static double WaterCutPct(double oil, double water)
        {
            var total = oil + water;
            return total <= 0 ? 0 : water / total * 100;
        }

        private static double SharePct(double oil, double totalOil)
        {
            return totalOil <= 0 ? 0 : oil / totalOil * 100;
        }

        private static string Format(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
