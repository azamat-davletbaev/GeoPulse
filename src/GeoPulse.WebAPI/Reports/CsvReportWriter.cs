using System.Globalization;
using System.Text;
using GeoPulse.WebAPI.Models.Reports;

namespace GeoPulse.WebAPI.Reports
{
    /// <summary>
    /// Формирование CSV-выгрузки (запятая, BOM, заголовки на английском) без внешних библиотек.
    /// Использует те же посчитанные строки, что и XLSX, — расчёты не дублируются.
    /// </summary>
    public sealed class CsvReportWriter
    {
        /// <summary>Числовой формат CSV: два знака после запятой, InvariantCulture.</summary>
        private const string ValueFormat = "0.00";

        /// <summary>CSV по месторождению: `Year,Month,OilVol,WaterVol,WaterCutPct`.</summary>
        /// <param name="data">Подготовленные данные отчёта.</param>
        public byte[] BuildMonthlyCsv(MonthlyReportData data)
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

        private static string Format(double value) => value.ToString(ValueFormat, CultureInfo.InvariantCulture);
    }
}
