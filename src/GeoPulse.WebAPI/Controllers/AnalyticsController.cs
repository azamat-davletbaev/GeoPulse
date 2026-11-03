using GeoPulse.Core.Models;
using GeoPulse.Core.Repositories;
using GeoPulse.WebAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>Помесячная аналитика добычи (ClickHouse gp.monthly_production; Postgres-схема analytics заморожена).</summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AnalyticsController : ControllerBase
    {
        private readonly IWellRepository _wells;
        private readonly ClickHouseAnalyticsService _analytics;

        /// <summary>Конструктор.</summary>
        public AnalyticsController(IWellRepository wells, ClickHouseAnalyticsService analytics)
        {
            _wells = wells;
            _analytics = analytics;
        }

        /// <summary>Помесячные агрегаты одной скважины (адресация по внутреннему id или коду НПД).</summary>
        /// <param name="wellId">Внутренний идентификатор скважины (wells.Id).</param>
        /// <param name="wellCode">Код ствола в НПД (натуральный ключ) — прежний контракт.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("monthly")]
        [ProducesResponseType(typeof(IReadOnlyList<MonthlyProductionDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<MonthlyProductionDto>>> GetMonthly(
            [FromQuery] int? wellId,
            [FromQuery] int? wellCode,
            CancellationToken cancellationToken)
        {
            Well? well;
            if (wellId.HasValue)
            {
                well = await _wells.GetByIdAsync(wellId.Value, cancellationToken);
                if (well is null)
                {
                    return Problem(
                        title: "Скважина не найдена",
                        detail: $"Скважина с id {wellId.Value} отсутствует в базе.",
                        statusCode: StatusCodes.Status404NotFound);
                }
            }
            else if (wellCode.HasValue)
            {
                well = await _wells.GetByNpdWellBoreCodeAsync(wellCode.Value, cancellationToken);
                if (well is null)
                {
                    return Problem(
                        title: "Скважина не найдена",
                        detail: $"Скважина с кодом НПД {wellCode.Value} отсутствует в базе.",
                        statusCode: StatusCodes.Status404NotFound);
                }
            }
            else
            {
                ModelState.AddModelError("wellId", "Укажите wellId или wellCode.");
                return ValidationProblem(ModelState);
            }

            var rows = await _analytics.GetMonthlyByWellAsync(well.Id, cancellationToken);

            return Ok(rows);
        }

        /// <summary>Суммарная добыча по всем скважинам за всё время (один SQL-запрос).</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(ProductionSummaryDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<ProductionSummaryDto>> GetSummary(CancellationToken cancellationToken)
        {
            return Ok(await _analytics.GetSummaryAsync(cancellationToken));
        }
    }
}
