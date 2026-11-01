using GeoPulse.WebAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>Скважины и стволы, загруженные из исходных данных Volve.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class WellsController : ControllerBase
    {
        private readonly GeoPulseDbContext _db;

        /// <summary>Конструктор.</summary>
        public WellsController(GeoPulseDbContext db)
        {
            _db = db;
        }

        /// <summary>Список скважин с количеством стволов.</summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetWells(CancellationToken cancellationToken)
        {
            var wells = await _db.Wells
                .AsNoTracking()
                .OrderBy(well => well.Name)
                .Select(well => new
                {
                    well.Id,
                    well.Name,
                    well.FieldName,
                    well.Operator,
                    Wellbores = well.Wellbores.Count
                })
                .ToListAsync(cancellationToken);

            return Ok(wells);
        }

        /// <summary>Стволы скважины.</summary>
        /// <param name="wellId">Идентификатор скважины.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet("{wellId:int}/wellbores")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWellbores(int wellId, CancellationToken cancellationToken)
        {
            var exists = await _db.Wells.AnyAsync(well => well.Id == wellId, cancellationToken);
            if (!exists)
            {
                return NotFound();
            }

            var wellbores = await _db.Wellbores
                .AsNoTracking()
                .Where(wellbore => wellbore.WellId == wellId)
                .OrderBy(wellbore => wellbore.Name)
                .Select(wellbore => new
                {
                    wellbore.Id,
                    wellbore.Name,
                    wellbore.NpdWellboreCode,
                    SurveyReports = wellbore.SurveyReports.Count,
                    DrillingReports = wellbore.DrillingReports.Count
                })
                .ToListAsync(cancellationToken);

            return Ok(wellbores);
        }
    }
}
