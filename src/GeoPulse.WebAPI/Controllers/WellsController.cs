using GeoPulse.Infrastructure;
using GeoPulse.WebAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>Скважины из основной модели данных.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class WellsController : ControllerBase
    {
        private readonly CoreDbContext _db;

        /// <summary>Конструктор.</summary>
        public WellsController(CoreDbContext db)
        {
            _db = db;
        }

        /// <summary>Список скважин.</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<WellDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetWells(CancellationToken cancellationToken)
        {
            var wells = await _db.Wells
                .AsNoTracking()
                .OrderBy(well => well.Name)
                .ToListAsync(cancellationToken);

            var result = wells
                .Select(well => new WellDto
                {
                    Id = well.Id,
                    NpdWellBoreCode = well.NpdWellBoreCode,
                    Name = well.Name,
                    Field = well.Field,
                    Type = well.Type
                })
                .ToList();

            return Ok(result);
        }
    }
}
