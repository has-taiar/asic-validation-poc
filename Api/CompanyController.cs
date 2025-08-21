using Microsoft.AspNetCore.Mvc;
using AsicValidationPoc.Services;

namespace AsicValidationPoc.Api
{
    [Route("api/company")]
    [ApiController]
    public class CompanyController : ControllerBase
    {
        private readonly CompanyLookupService _service;
        private readonly ILogger<CompanyController> _logger;
        
        public CompanyController(CompanyLookupService service, ILogger<CompanyController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query)
        {
            _logger.LogInformation("API search request received for query: '{Query}'", query ?? "");
            
            try
            {
                var results = await _service.SearchCompaniesAsync(query ?? "");
                var response = results.Select(c => new { 
                    name = c.Name,
                    licenseType = c.LicenseType,
                    tradingAs = c.TradingAs,
                    licenseDate = c.LicenseDate,
                    acn = c.ACN
                });
                
                _logger.LogInformation("API returning {Count} results for query: '{Query}'", results.Count, query ?? "");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing search request for query: '{Query}'", query ?? "");
                return StatusCode(500, new { error = "An error occurred while searching companies" });
            }
        }
    }
}
