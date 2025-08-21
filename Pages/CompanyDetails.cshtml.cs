using Microsoft.AspNetCore.Mvc.RazorPages;
using AsicValidationPoc.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace AsicValidationPoc.Pages
{
    public class CompanyDetailsModel : PageModel
    {
        private readonly CompanyLookupService _service;
    public CompanyInfo? Company { get; set; }

        public CompanyDetailsModel(CompanyLookupService service)
        {
            _service = service;
        }

        public async Task<IActionResult> OnGetAsync(string name)
        {
            if (string.IsNullOrEmpty(name)) return NotFound();
            Company = await _service.GetCompanyDetailsAsync(name);
            if (Company == null) return NotFound();
            return Page();
        }
    }
}
