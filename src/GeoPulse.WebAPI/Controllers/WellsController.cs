using GeoPulse.Core.Geometry;
using GeoPulse.Core.Repositories;
using GeoPulse.WebAPI.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>
    /// Скважины и их суточная добыча — точечные данные из PostgreSQL.
    /// Все ресурсы скважины адресуются натуральным ключом npdWellBoreCode: он стабилен
    /// при переимпорте, в отличие от identity-идентификатора wells.id.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class WellsController : ControllerBase
    {
        private readonly IWellRepository _wells;
        private readonly IDailyProductionRepository _production;
        private readonly IWellTrajectoryRepository _trajectories;

        /// <summary>Конструктор.</summary>
        public WellsController(
            IWellRepository wells,
            IDailyProductionRepository production,
            IWellTrajectoryRepository trajectories)
        {
            _wells = wells;
            _production = production;
            _trajectories = trajectories;
        }

        /// <summary>Список скважин с координатами устья (JOIN wells + well_locations).</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<WellDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<WellDto>>> GetWells(CancellationToken cancellationToken)
        {
            var wells = await _wells.GetAllWithLocationsAsync(cancellationToken);

            return Ok(wells.Select(WellDto.FromModel).ToList());
        }

        /// <summary>Траектория ствола: замеры (MD/TVD/инклинометрия) в WGS84 (конвертация из ED50/UTM31N на бэкенде).</summary>
        /// <param name="npdWellBoreCode">Код ствола в НПД (натуральный ключ).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("{npdWellBoreCode:int}/trajectory")]
        [ProducesResponseType(typeof(IReadOnlyList<TrajectoryPointDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<TrajectoryPointDto>>> GetTrajectory(
            int npdWellBoreCode,
            CancellationToken cancellationToken)
        {
            var well = await _wells.GetByNpdWellBoreCodeAsync(npdWellBoreCode, cancellationToken);
            if (well is null)
            {
                return WellNotFound(npdWellBoreCode);
            }

            var points = await _trajectories.GetByWellIdAsync(well.Id, cancellationToken);

            return Ok(points.Select(point =>
            {
                var (latitude, longitude) = CoordinateTransform.Ed50Utm31ToWgs84(point.Easting, point.Northing);
                return new TrajectoryPointDto
                {
                    Md = point.Md,
                    Tvd = point.Tvd,
                    Inclination = point.Inclination,
                    Azimuth = point.Azimuth,
                    Latitude = latitude,
                    Longitude = longitude
                };
            }).ToList());
        }

        /// <summary>Карточка скважины по коду НПД: справочные данные и покрытие добычей.</summary>
        /// <param name="npdWellBoreCode">Код ствола в НПД (натуральный ключ).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("{npdWellBoreCode:int}")]
        [ProducesResponseType(typeof(WellDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<WellDetailsDto>> GetWellDetails(
            int npdWellBoreCode,
            CancellationToken cancellationToken)
        {
            var well = await _wells.GetByNpdWellBoreCodeAsync(npdWellBoreCode, cancellationToken);
            if (well is null)
            {
                return WellNotFound(npdWellBoreCode);
            }

            var coverage = await _production.GetCoverageAsync(well.Id, cancellationToken);
            return Ok(WellDetailsDto.FromModel(well, coverage));
        }

        /// <summary>Суточная добыча скважины: фильтр по диапазону дат, сортировка и пагинация.</summary>
        /// <param name="npdWellBoreCode">Код ствола в НПД (натуральный ключ).</param>
        /// <param name="parameters">Диапазон дат, страница и порядок сортировки.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("{npdWellBoreCode:int}/production")]
        [ProducesResponseType(typeof(PagedResponse<ProductionRecordDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PagedResponse<ProductionRecordDto>>> GetProduction(
            int npdWellBoreCode,
            [FromQuery] ProductionQueryParameters parameters,
            CancellationToken cancellationToken)
        {
            foreach (var error in parameters.Validate())
            {
                ModelState.AddModelError(error.Key, error.Value);
            }

            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var well = await _wells.GetByNpdWellBoreCodeAsync(npdWellBoreCode, cancellationToken);
            if (well is null)
            {
                return WellNotFound(npdWellBoreCode);
            }

            var page = await _production.GetPageAsync(well.Id, parameters.ToQuery(), cancellationToken);
            var items = page.Items.Select(ProductionRecordDto.FromModel).ToList();

            return Ok(PagedResponse<ProductionRecordDto>.From(page, items));
        }

        /// <summary>Суточная добыча скважины за конкретную дату.</summary>
        /// <param name="npdWellBoreCode">Код ствола в НПД (натуральный ключ).</param>
        /// <param name="date">Дата в формате yyyy-MM-dd.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("{npdWellBoreCode:int}/production/{date}")]
        [ProducesResponseType(typeof(ProductionRecordDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProductionRecordDto>> GetProductionByDate(
            int npdWellBoreCode,
            DateOnly date,
            CancellationToken cancellationToken)
        {
            var well = await _wells.GetByNpdWellBoreCodeAsync(npdWellBoreCode, cancellationToken);
            if (well is null)
            {
                return WellNotFound(npdWellBoreCode);
            }

            var record = await _production.GetByDateAsync(well.Id, date, cancellationToken);
            if (record is null)
            {
                return Problem(
                    title: "Запись не найдена",
                    detail: $"Для скважины {npdWellBoreCode} нет данных добычи за {date:yyyy-MM-dd}.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Ok(ProductionRecordDto.FromModel(record));
        }

        /// <summary>ProblemDetails для отсутствующей скважины.</summary>
        private ObjectResult WellNotFound(int npdWellBoreCode) =>
            Problem(
                title: "Скважина не найдена",
                detail: $"Скважина с кодом НПД {npdWellBoreCode} отсутствует в базе.",
                statusCode: StatusCodes.Status404NotFound);
    }
}
