using GeoPulse.WebAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace GeoPulse.WebAPI.Controllers
{
    /// <summary>
    /// 
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ProductionController : ControllerBase
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("wells")]
        [ProducesResponseType(typeof(IEnumerable<WellDto>), StatusCodes.Status200OK)]
        public IActionResult GetWells()
        {
            var wells = new[]
            {
                new WellDto { NpdWellBoreCode = 557, Name = "15/9-F-11", OilRate = 8550.55 },
                new WellDto { NpdWellBoreCode = 563, Name = "15/9-F-12", OilRate = 6120.10 }
            };
            return Ok(wells);
        }
    }
}