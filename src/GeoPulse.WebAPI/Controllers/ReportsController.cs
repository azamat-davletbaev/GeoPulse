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

        /// <summary>Excel-отчёт по одной скважине: листы «Скважина» (имя, месторождение, суммарные
        /// нефть/вода, период, доля) и «Помесячно» (год, месяц, нефть, вода, обводнённость).</summary>
        /// <param name="wellId">Внутренний идентификатор скважины (wells.Id).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("wells/{wellId:int}.xlsx")]
        [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWellExcel(int wellId, CancellationToken cancellationToken)
        {
            var data = await _reports.LoadWellAsync(wellId, cancellationToken);
            if (data is null)
            {
                return Problem(
                    title: "Скважина не найдена",
                    detail: $"Скважина с id {wellId} отсутствует в базе.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var bytes = _reports.BuildWellXlsx(data);

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"well_{wellId}_{DateTime.Now:yyyy-MM-dd}.xlsx");
        }
    }
}
