using GeoPulse.Core.Repositories;
using GeoPulse.WebAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>Помесячная аналитика добычи (схема analytics).</summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AnalyticsController : ControllerBase
    {
        private readonly IWellRepository _wells;
        private readonly IAnalyticsRepository _analytics;

        /// <summary>Конструктор.</summary>
        public AnalyticsController(IWellRepository wells, IAnalyticsRepository analytics)
        {
            _wells = wells;
            _analytics = analytics;
        }

        /// <summary>Помесячные агрегаты одной скважины (адресация по коду НПД).</summary>
        /// <param name="wellCode">Код ствола в НПД (натуральный ключ).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("monthly")]
        [ProducesResponseType(typeof(IReadOnlyList<MonthlyProductionDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<MonthlyProductionDto>>> GetMonthly(
            [FromQuery] int wellCode,
            CancellationToken cancellationToken)
        {
            var well = await _wells.GetByNpdWellBoreCodeAsync(wellCode, cancellationToken);
            if (well is null)
            {
                return Problem(
                    title: "Скважина не найдена",
                    detail: $"Скважина с кодом НПД {wellCode} отсутствует в базе.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var rows = await _analytics.GetMonthlyByWellAsync(well.Id, cancellationToken);

            return Ok(rows.Select(MonthlyProductionDto.FromModel).ToList());
        }

        /// <summary>Суммарная добыча по всем скважинам за всё время (один SQL-запрос).</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(ProductionSummaryDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<ProductionSummaryDto>> GetSummary(CancellationToken cancellationToken)
        {
            var summary = await _analytics.GetSummaryAsync(cancellationToken);

            return Ok(summary is null ? new ProductionSummaryDto() : ProductionSummaryDto.FromModel(summary));
        }
    }
}
