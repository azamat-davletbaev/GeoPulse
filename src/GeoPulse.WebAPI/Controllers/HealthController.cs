using GeoPulse.Infrastructure;
using GeoPulse.WebAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>Проверка работоспособности API и доступности PostgreSQL.</summary>
    [ApiController]
    [Route("health")]
    [Produces("application/json")]
    public class HealthController : ControllerBase
    {
        private readonly CoreDbContext _db;
        private readonly ILogger<HealthController> _logger;

        /// <summary>Конструктор.</summary>
        public HealthController(CoreDbContext db, ILogger<HealthController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>Состояние API и подключения к БД.</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        [HttpGet]
        [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
        {
            var databaseAvailable = false;

            try
            {
                databaseAvailable = await _db.Database.CanConnectAsync(cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(exception, "Проверка подключения к PostgreSQL завершилась ошибкой");
            }

            var response = new HealthResponse
            {
                Status = databaseAvailable ? "ok" : "degraded",
                Database = databaseAvailable ? "up" : "down",
                CheckedAtUtc = DateTimeOffset.UtcNow
            };

            return databaseAvailable
                ? Ok(response)
                : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
        }
    }
}
