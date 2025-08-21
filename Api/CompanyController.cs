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

        [HttpGet("export-csv")]
        public async Task<IActionResult> ExportCsv([FromQuery] bool enriched = false)
        {
            _logger.LogInformation("CSV export request received (enriched: {Enriched})", enriched);
            
            try
            {
                var companies = enriched ? 
                    await _service.GetAllCompaniesWithDetailsAsync() : 
                    await _service.GetCompaniesAsync();
                    
                _logger.LogInformation("Exporting {Count} companies to CSV", companies.Count);
                
                var csv = GenerateCsv(companies);
                var fileName = $"vic_licensed_companies_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                
                return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting companies to CSV");
                return StatusCode(500, new { error = "An error occurred while exporting data" });
            }
        }

        private string GenerateCsv(List<CompanyInfo> companies)
        {
            var csv = new System.Text.StringBuilder();
            
            // CSV Header
            csv.AppendLine("Company Name,License Type,Trading As,License Date,ACN,Details URL,Other Details");
            
            // CSV Data
            foreach (var company in companies)
            {
                var name = EscapeCsvField(company.Name ?? "");
                var licenseType = EscapeCsvField(company.LicenseType ?? "");
                var tradingAs = EscapeCsvField(company.TradingAs ?? "");
                var licenseDate = EscapeCsvField(company.LicenseDate ?? "");
                var acn = EscapeCsvField(company.ACN ?? "");
                var detailsUrl = EscapeCsvField(company.DetailsUrl ?? "");
                var otherDetails = EscapeCsvField(company.OtherDetails ?? "");
                
                csv.AppendLine($"{name},{licenseType},{tradingAs},{licenseDate},{acn},{detailsUrl},{otherDetails}");
            }
            
            return csv.ToString();
        }

        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "";
                
            // If field contains comma, newline, or quote, wrap in quotes and escape internal quotes
            if (field.Contains(',') || field.Contains('\n') || field.Contains('\r') || field.Contains('"'))
            {
                field = field.Replace("\"", "\"\""); // Escape quotes by doubling them
                return $"\"{field}\"";
            }
            
            return field;
        }
    }
}
