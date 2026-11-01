using GeoPulse.WebAPI.Data;
using GeoPulse.WebAPI.Data.Entities;
using GeoPulse.WebAPI.Data.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>
    /// Импорт исходных данных Volve (папка <c>xml</c>) в PostgreSQL.
    /// Данные пишутся пакетами по 1000 записей.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ImportController : ControllerBase
    {
        private readonly VolveImportService _importService;
        private readonly GeoPulseDbContext _db;

        /// <summary>Конструктор.</summary>
        public ImportController(VolveImportService importService, GeoPulseDbContext db)
        {
            _importService = importService;
            _db = db;
        }

        /// <summary>Запускает импорт: отчёты бурения, траектории и добычу (пакетами по 1000 записей).</summary>
        /// <returns>Счётчики загруженных записей и количество пакетов.</returns>
        [HttpPost("volve")]
        [ProducesResponseType(typeof(ImportResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> ImportVolve(CancellationToken cancellationToken)
        {
            var result = await _importService.ImportAsync(cancellationToken);
            return Ok(result);
        }

        /// <summary>Журнал запусков импорта (последние 50).</summary>
        /// <returns>Список записей <c>import_runs</c>.</returns>
        [HttpGet("runs")]
        [ProducesResponseType(typeof(IEnumerable<ImportRun>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRuns(CancellationToken cancellationToken)
        {
            var runs = await _db.ImportRuns
                .AsNoTracking()
                .OrderByDescending(run => run.StartedAt)
                .Take(50)
                .ToListAsync(cancellationToken);

            return Ok(runs);
        }
    }
}
