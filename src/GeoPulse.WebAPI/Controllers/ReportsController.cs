using GeoPulse.WebAPI.Reports;
using Microsoft.AspNetCore.Mvc;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>Выгрузка отчётов по добыче месторождения (Excel/CSV).</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private const string XlsxContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly ReportDataLoader _data;
        private readonly XlsxReportWriter _xlsx;
        private readonly CsvReportWriter _csv;

        /// <summary>Конструктор.</summary>
        public ReportsController(ReportDataLoader data, XlsxReportWriter xlsx, CsvReportWriter csv)
        {
            _data = data;
            _xlsx = xlsx;
            _csv = csv;
        }

        /// <summary>Excel-отчёт по месторождению: листы «Сводка», «Помесячно», «По скважинам».</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("monthly.xlsx")]
        [Produces(XlsxContentType)]
        public async Task<IActionResult> GetMonthlyExcel(CancellationToken cancellationToken)
        {
            var data = await _data.LoadAsync(cancellationToken);

            return File(_xlsx.BuildMonthlyXlsx(data), XlsxContentType, $"monthly_{DateTime.Now:yyyy-MM-dd}.xlsx");
        }

        /// <summary>CSV-отчёт: тот же помесячный набор (запятая, BOM, заголовки на английском).</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("monthly.csv")]
        [Produces("text/csv")]
        public async Task<IActionResult> GetMonthlyCsv(CancellationToken cancellationToken)
        {
            var data = await _data.LoadAsync(cancellationToken);

            return File(_csv.BuildMonthlyCsv(data), "text/csv", $"monthly_{DateTime.Now:yyyy-MM-dd}.csv");
        }

        /// <summary>Excel-отчёт по одной скважине: листы «Скважина» (имя, месторождение, суммарные
        /// нефть/вода, период, доля) и «Помесячно» (год, месяц, нефть, вода, обводнённость).</summary>
        /// <param name="npdWellBoreCode">Код ствола в НПД (натуральный ключ).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("wells/{npdWellBoreCode:int}.xlsx")]
        [Produces(XlsxContentType)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWellExcel(int npdWellBoreCode, CancellationToken cancellationToken)
        {
            var data = await _data.LoadWellAsync(npdWellBoreCode, cancellationToken);
            if (data is null)
            {
                return Problem(
                    title: "Скважина не найдена",
                    detail: $"Скважина с кодом НПД {npdWellBoreCode} отсутствует в базе.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return File(
                _xlsx.BuildWellXlsx(data),
                XlsxContentType,
                $"well_{npdWellBoreCode}_{DateTime.Now:yyyy-MM-dd}.xlsx");
        }
    }
}
