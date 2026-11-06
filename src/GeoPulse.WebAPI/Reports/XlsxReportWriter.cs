using ClosedXML.Excel;
using GeoPulse.WebAPI.Models.Reports;

namespace GeoPulse.WebAPI.Reports
{
    /// <summary>
    /// Формирование XLSX-выгрузок (ClosedXML). Класс отвечает только за разметку листов и стили;
    /// данные и расчёты приходят готовыми из <see cref="ReportDataLoader"/>.
    /// Сознательно без ArrayPool и параллелизации: объём — сотни строк, генерация занимает
    /// единицы миллисекунд; оптимизации оправданы на порядки 10^5 строк.
    /// </summary>
    public sealed class XlsxReportWriter
    {
        /// <summary>Единый числовой формат всех отчётов (`#,##0.00`).</summary>
        private const string NumberFormat = "#,##0.00";

        /// <summary>XLSX по месторождению: листы «Сводка», «Помесячно», «По скважинам».</summary>
        /// <param name="data">Подготовленные данные отчёта.</param>
        public byte[] BuildMonthlyXlsx(MonthlyReportData data)
        {
            using var workbook = new XLWorkbook();
            BuildSummarySheet(workbook, data);
            BuildMonthlySheet(workbook, "Помесячно", data.Monthly);
            BuildWellsSheet(workbook, data);
            return Save(workbook);
        }

        /// <summary>
        /// XLSX по одной скважине: листы «Скважина» и «Помесячно». Лист «Помесячно» и стили
        /// строятся теми же функциями, что и в отчёте по месторождению.
        /// </summary>
        /// <param name="data">Подготовленные данные скважины.</param>
        public byte[] BuildWellXlsx(WellReportData data)
        {
            using var workbook = new XLWorkbook();
            BuildWellSheet(workbook, data);
            BuildMonthlySheet(workbook, "Помесячно", data.Monthly);
            return Save(workbook);
        }

        private static byte[] Save(XLWorkbook workbook)
        {
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
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
            sheet.Columns().AdjustToContents();
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
            sheet.Columns().AdjustToContents();
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

            StyleHeader(sheet.Range(1, 1, 1, 4));
            if (row > 2)
            {
                StyleNumbers(sheet.Range(2, 2, row - 1, 4));
            }

            sheet.Columns().AdjustToContents();
        }

        /// <summary>Общая шапка всех листов: жирная строка заголовков.</summary>
        private static void StyleHeader(IXLRange header) => header.Style.Font.Bold = true;

        /// <summary>Общий числовой формат всех отчётов — один и тот же `#,##0.00`.</summary>
        private static void StyleNumbers(IXLRange range) => range.Style.NumberFormat.Format = NumberFormat;
    }
}
