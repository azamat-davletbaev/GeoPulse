using GeoPulse.Core.Repositories;
using GeoPulse.WebAPI.Analytics;
using GeoPulse.WebAPI.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>Помесячная аналитика добычи (источник — ClickHouse gp.monthly_production).</summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AnalyticsController : ControllerBase
    {
        private readonly IWellRepository _wells;
        private readonly IMonthlyProductionReader _analytics;

        /// <summary>Конструктор.</summary>
        public AnalyticsController(IWellRepository wells, IMonthlyProductionReader analytics)
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
            var well = wellId.HasValue
                ? await _wells.GetByIdAsync(wellId.Value, cancellationToken)
                : wellCode.HasValue
                    ? await _wells.GetByNpdWellBoreCodeAsync(wellCode.Value, cancellationToken)
                    : null;

            if (well is null)
            {
                if (!wellId.HasValue && !wellCode.HasValue)
                {
                    ModelState.AddModelError("wellId", "Укажите wellId или wellCode.");
                    return ValidationProblem(ModelState);
                }

                return Problem(
                    title: "Скважина не найдена",
                    detail: $"Скважина с {(wellId.HasValue ? $"id {wellId.Value}" : $"кодом НПД {wellCode!.Value}")} отсутствует в базе.",
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

            return Ok(ProductionSummaryDto.FromModel(summary));
        }
    }
}
