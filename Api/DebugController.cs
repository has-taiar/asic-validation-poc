using Microsoft.AspNetCore.Mvc;
using AsicValidationPoc.Services;

namespace AsicValidationPoc.Api
{
    [Route("api/debug")]
    [ApiController]
    public class DebugController : ControllerBase
    {
        private readonly CompanyLookupService _service;
        
        public DebugController(CompanyLookupService service)
        {
            _service = service;
        }

        [HttpPost("clear-cache")]
        public IActionResult ClearCache()
        {
            _service.ClearCache();
            return Ok(new { message = "Cache cleared" });
        }
    }
}
