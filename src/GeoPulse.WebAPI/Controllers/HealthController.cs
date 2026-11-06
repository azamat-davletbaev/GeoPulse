using GeoPulse.WebAPI.Health;
using GeoPulse.WebAPI.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>Проверка работоспособности API и доступности PostgreSQL.</summary>
    [ApiController]
    [Route("health")]
    [Produces("application/json")]
    public class HealthController : ControllerBase
    {
        private readonly IDatabaseHealthProbe _database;
        private readonly ILogger<HealthController> _logger;

        /// <summary>Конструктор.</summary>
        public HealthController(IDatabaseHealthProbe database, ILogger<HealthController> logger)
        {
            _database = database;
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
                databaseAvailable = await _database.CanConnectAsync(cancellationToken);
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
