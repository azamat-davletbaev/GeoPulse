using Microsoft.AspNetCore.Mvc;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>Выгрузка отчётов по добыче месторождения (Excel/CSV).</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly ReportService _reports;

        /// <summary>Конструктор.</summary>
        public ReportsController(ReportService reports)
        {
            _reports = reports;
        }

        /// <summary>Excel-отчёт по месторождению: листы «Сводка», «Помесячно», «По скважинам».</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("monthly.xlsx")]
        [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
        public async Task<IActionResult> GetMonthlyExcel(CancellationToken cancellationToken)
        {
            var data = await _reports.LoadAsync(cancellationToken);
            var bytes = _reports.BuildXlsx(data);

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"monthly_{DateTime.Now:yyyy-MM-dd}.xlsx");
        }

        /// <summary>CSV-отчёт: тот же помесячный набор (запятая, BOM, заголовки на английском).</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("monthly.csv")]
        [Produces("text/csv")]
        public async Task<IActionResult> GetMonthlyCsv(CancellationToken cancellationToken)
        {
            var data = await _reports.LoadAsync(cancellationToken);
            var bytes = _reports.BuildCsv(data);

            return File(bytes, "text/csv", $"monthly_{DateTime.Now:yyyy-MM-dd}.csv");
        }
    }
}
