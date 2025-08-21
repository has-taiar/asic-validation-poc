using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace AsicValidationPoc.Services
{
    public class CompanyInfo
    {
        public string? Name { get; set; }
        public string? LicenseType { get; set; }
        public string? ACN { get; set; }
        public string? DetailsUrl { get; set; }
        public string? OtherDetails { get; set; }
        public string? TradingAs { get; set; }
        public string? LicenseDate { get; set; }
    }

    public class CompanyLookupService
    {
        private static readonly string SourceUrl = "https://www.esc.vic.gov.au/electricity-and-gas/electricity-and-gas-licences-and-exemptions/electricity-and-gas-licences#tabs-container2";
        private static List<CompanyInfo>? _companies;
        private static DateTime _lastFetch;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);
        private readonly ILogger<CompanyLookupService> _logger;

        public CompanyLookupService(ILogger<CompanyLookupService> logger)
        {
            _logger = logger;
        }

        public async Task<List<CompanyInfo>> GetCompaniesAsync()
        {
            _logger.LogInformation("Starting company data fetch...");
            
            if (_companies != null && DateTime.Now - _lastFetch < CacheDuration)
            {
                _logger.LogInformation("Returning cached data with {Count} companies", _companies.Count);
                return _companies;
            }

            try
            {
                _logger.LogInformation("Fetching data from ESC VIC website: {Url}", SourceUrl);
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var html = await client.GetStringAsync(SourceUrl);
                _logger.LogInformation("Downloaded HTML content, length: {Length} characters", html.Length);
                
                _companies = ParseCompaniesFromHtml(html);
                _lastFetch = DateTime.Now;
                _logger.LogInformation("Parsed {Count} companies from HTML", _companies.Count);
                
                return _companies ?? new List<CompanyInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching company data from ESC VIC website");
                return _companies ?? new List<CompanyInfo>();
            }
        }

        public void ClearCache()
        {
            _companies = null;
            _logger.LogInformation("Cache cleared");
        }

        private List<CompanyInfo> ParseCompaniesFromHtml(string html)
        {
            var companies = new List<CompanyInfo>();
            _logger.LogInformation("Starting HTML parsing...");

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // First, let's debug the HTML structure by looking for headings
                var allHeadings = doc.DocumentNode.SelectNodes("//h2 | //h3 | //h4");
                if (allHeadings != null)
                {
                    _logger.LogInformation("Found {Count} headings in the document", allHeadings.Count);
                    foreach (var heading in allHeadings.Take(10))
                    {
                        _logger.LogInformation("Heading found: '{Text}' (tag: {Tag})", heading.InnerText.Trim(), heading.Name);
                    }
                }

                // Look for different license section headings with more flexible matching
                var sections = new Dictionary<string, string>
                {
                    { "electricity retail licences", "Electricity Retail" },
                    { "electricity distribution licences", "Electricity Distribution" },
                    { "gas retail licences", "Gas Retail" },
                    { "gas distribution licences", "Gas Distribution" },
                    { "electricity generation licences", "Electricity Generation" },
                    { "electricity transmission licences", "Electricity Transmission" }
                };

                foreach (var section in sections)
                {
                    _logger.LogInformation("Processing section: {SectionName}", section.Key);
                    var sectionCompanies = ParseSection(doc, section.Key, section.Value);
                    companies.AddRange(sectionCompanies);
                    _logger.LogInformation("Found {Count} companies in {SectionName} section", sectionCompanies.Count, section.Key);
                }

                // If no companies found with h2 sections, try alternative parsing
                if (companies.Count == 0)
                {
                    _logger.LogInformation("No companies found with section-based parsing, trying alternative approach...");
                    companies = ParseAlternativeApproach(doc);
                }

                _logger.LogInformation("Total companies parsed: {TotalCount}", companies.Count);
                
                // Log some sample company names for debugging
                if (companies.Any())
                {
                    var sampleNames = companies.Take(5).Select(c => c.Name).ToList();
                    _logger.LogInformation("Sample company names: {SampleNames}", string.Join(", ", sampleNames));
                }
                
                return companies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing HTML content");
                return new List<CompanyInfo>();
            }
        }

        private List<CompanyInfo> ParseAlternativeApproach(HtmlDocument doc)
        {
            var companies = new List<CompanyInfo>();
            
            try
            {
                // Look for all links that might be company licenses
                var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
                if (allLinks != null)
                {
                    _logger.LogInformation("Found {Count} total links in document", allLinks.Count);
                    
                    foreach (var link in allLinks)
                    {
                        var href = link.GetAttributeValue("href", "");
                        var linkText = link.InnerText.Trim();
                        
                        // Look for links that seem to be company licenses
                        if (IsCompanyLicenseLink(href, linkText))
                        {
                            var company = ParseCompanyFromLink(link, href, linkText);
                            if (company != null)
                            {
                                companies.Add(company);
                            }
                        }
                    }
                    
                    _logger.LogInformation("Alternative parsing found {Count} companies", companies.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in alternative parsing approach");
            }
            
            return companies;
        }

        private bool IsCompanyLicenseLink(string href, string linkText)
        {
            // Check if this looks like a company license link
            return !string.IsNullOrEmpty(linkText) &&
                   !linkText.Contains("Standard Licence", StringComparison.OrdinalIgnoreCase) &&
                   !linkText.Contains(".pdf", StringComparison.OrdinalIgnoreCase) &&
                   !href.Contains(".pdf") &&
                   (href.Contains("electricity-and-gas-licences") || 
                    href.Contains("licences-exemptions-and-trial-waivers")) &&
                   (linkText.Contains("Pty Ltd") || 
                    linkText.Contains("Limited") || 
                    linkText.Contains("Partnership") ||
                    linkText.Contains("Corporation") ||
                    linkText.Contains("Licence"));
        }

        private CompanyInfo? ParseCompanyFromLink(HtmlNode link, string href, string linkText)
        {
            try
            {
                // Determine license type from URL or surrounding context
                var licenseType = DetermineLicenseType(href, linkText);
                
                // Clean up company name
                var companyName = CleanCompanyName(linkText);
                
                if (string.IsNullOrEmpty(companyName))
                    return null;

                // Try to extract date from surrounding text
                var parentText = link.ParentNode?.InnerText ?? "";
                var dateText = "";
                var tradingAs = "";
                
                // Look for date patterns in surrounding content
                var dateMatch = Regex.Match(parentText, @"\b(\d{1,2}\s+\w+\s+\d{4})\b");
                if (dateMatch.Success)
                {
                    dateText = dateMatch.Groups[1].Value;
                }

                // Look for "Trading as" information
                var tradingMatch = Regex.Match(parentText, @"Trading as (.+?)(?:\n|$|\.)", RegexOptions.IgnoreCase);
                if (tradingMatch.Success)
                {
                    tradingAs = tradingMatch.Groups[1].Value.Trim();
                }

                return new CompanyInfo
                {
                    Name = companyName,
                    LicenseType = licenseType,
                    DetailsUrl = href.StartsWith("http") ? href : $"https://www.esc.vic.gov.au{href}",
                    LicenseDate = dateText,
                    TradingAs = tradingAs,
                    OtherDetails = $"License Date: {dateText}{(string.IsNullOrEmpty(tradingAs) ? "" : $", Trading As: {tradingAs}")}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing company from link: {LinkText}", linkText);
                return null;
            }
        }

        private string DetermineLicenseType(string href, string linkText)
        {
            var lowerHref = href.ToLowerInvariant();
            var lowerText = linkText.ToLowerInvariant();
            
            if (lowerHref.Contains("electricity-retail") || lowerText.Contains("electricity retail"))
                return "Electricity Retail";
            if (lowerHref.Contains("electricity-distribution") || lowerText.Contains("electricity distribution"))
                return "Electricity Distribution";
            if (lowerHref.Contains("electricity-generation") || lowerText.Contains("electricity generation"))
                return "Electricity Generation";
            if (lowerHref.Contains("electricity-transmission") || lowerText.Contains("electricity transmission"))
                return "Electricity Transmission";
            if (lowerHref.Contains("gas-retail") || lowerText.Contains("gas retail"))
                return "Gas Retail";
            if (lowerHref.Contains("gas-distribution") || lowerText.Contains("gas distribution"))
                return "Gas Distribution";
            
            // Fallback based on text content
            if (lowerText.Contains("electricity") && lowerText.Contains("retail"))
                return "Electricity Retail";
            if (lowerText.Contains("electricity") && lowerText.Contains("distribution"))
                return "Electricity Distribution";
            if (lowerText.Contains("electricity") && lowerText.Contains("generation"))
                return "Electricity Generation";
            if (lowerText.Contains("electricity") && lowerText.Contains("transmission"))
                return "Electricity Transmission";
            if (lowerText.Contains("gas") && lowerText.Contains("retail"))
                return "Gas Retail";
            if (lowerText.Contains("gas") && lowerText.Contains("distribution"))
                return "Gas Distribution";
                
            return "Unknown License Type";
        }

        private List<CompanyInfo> ParseSection(HtmlDocument doc, string sectionHeading, string licenseType)
        {
            var companies = new List<CompanyInfo>();
            
            try
            {
                // Find the section heading with more flexible matching
                var headingNode = doc.DocumentNode
                    .SelectNodes("//h2 | //h3")
                    ?.FirstOrDefault(h => h.InnerText.Trim().Contains(sectionHeading, StringComparison.OrdinalIgnoreCase));

                if (headingNode == null)
                {
                    _logger.LogWarning("Could not find section heading: {SectionHeading}", sectionHeading);
                    
                    // Try to find by ID or other attributes
                    var idBasedNode = doc.DocumentNode.SelectSingleNode($"//*[contains(@id, '{sectionHeading.Replace(" ", "-").ToLowerInvariant()}')]");
                    if (idBasedNode != null)
                    {
                        headingNode = idBasedNode;
                        _logger.LogInformation("Found section by ID: {SectionHeading}", sectionHeading);
                    }
                    else
                    {
                        return companies;
                    }
                }

                _logger.LogInformation("Found heading node for section: {SectionHeading}", sectionHeading);

                // Get all content after this heading until the next heading
                var currentNode = headingNode.NextSibling;
                var processedNodes = 0;
                
                while (currentNode != null && processedNodes < 50) // Safety limit
                {
                    // Stop if we hit another major section heading
                    if (currentNode.NodeType == HtmlNodeType.Element && 
                        (currentNode.Name == "h2" || currentNode.Name == "h3"))
                    {
                        var nextHeadingText = currentNode.InnerText.Trim().ToLowerInvariant();
                        if (nextHeadingText.Contains("licence") && !nextHeadingText.Contains(sectionHeading.ToLowerInvariant()))
                        {
                            break;
                        }
                    }

                    if (currentNode.NodeType == HtmlNodeType.Element)
                    {
                        // Look for links that contain company information
                        var links = currentNode.SelectNodes(".//a[@href]");
                        if (links != null)
                        {
                            foreach (var link in links)
                            {
                                var href = link.GetAttributeValue("href", "");
                                var linkText = link.InnerText.Trim();
                                
                                if (IsCompanyLicenseLink(href, linkText))
                                {
                                    var company = ParseCompanyFromLink(link, href, linkText);
                                    if (company != null)
                                    {
                                        company.LicenseType = licenseType; // Override with section-specific type
                                        companies.Add(company);
                                    }
                                }
                            }
                        }
                    }
                    
                    currentNode = currentNode.NextSibling;
                    processedNodes++;
                }
                
                _logger.LogInformation("Processed {ProcessedNodes} nodes in section {SectionHeading}", processedNodes, sectionHeading);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing section: {SectionHeading}", sectionHeading);
            }

            return companies;
        }

        private string CleanCompanyName(string rawName)
        {
            // Remove common suffixes and clean up the name
            var cleaned = rawName
                .Replace(" - Electricity Retail Licence", "")
                .Replace(" - Gas Retail Licence", "")
                .Replace(" - Electricity Distribution Licence", "")
                .Replace(" - Gas Distribution Licence", "")
                .Replace(" - Electricity Generation Licence", "")
                .Replace(" - Electricity Transmission Licence", "")
                .Replace(" – Electricity Retail Licence", "")
                .Replace(" – Gas Retail Licence", "")
                .Replace(" – Electricity Distribution Licence", "")
                .Replace(" – Gas Distribution Licence", "")
                .Replace(" – Electricity Generation Licence", "")
                .Replace(" – Electricity Transmission Licence", "")
                .Replace(" Electricity Generation and Sale Licence", "")
                .Replace(" - Electricity Generation and Sale Licence", "")
                .Replace(" – Electricity Generation and Sale Licence", "")
                .Trim();

            return cleaned;
        }

        public async Task<List<CompanyInfo>> SearchCompaniesAsync(string query)
        {
            _logger.LogInformation("Searching for companies with query: '{Query}'", query);
            
            var companies = await GetCompaniesAsync();
            
            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.LogInformation("Empty query, returning all {Count} companies", companies.Count);
                return companies;
            }

            var results = companies.Where(c => 
                (c.Name != null && c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (c.TradingAs != null && c.TradingAs.Contains(query, StringComparison.OrdinalIgnoreCase))
            ).ToList();
            
            _logger.LogInformation("Found {ResultCount} companies matching query '{Query}'", results.Count, query);
            
            if (results.Any())
            {
                var matchedNames = results.Take(5).Select(c => c.Name).ToList();
                _logger.LogInformation("Sample matched companies: {MatchedNames}", string.Join(", ", matchedNames));
            }
            
            return results;
        }

        public async Task<CompanyInfo?> GetCompanyDetailsAsync(string name)
        {
            _logger.LogInformation("Getting details for company: '{CompanyName}'", name);
            
            var companies = await GetCompaniesAsync();
            var company = companies.FirstOrDefault(c => 
                (c.Name != null && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                (c.TradingAs != null && c.TradingAs.Equals(name, StringComparison.OrdinalIgnoreCase))
            );
            
            if (company != null)
            {
                _logger.LogInformation("Found company details for: {CompanyName}", company.Name);
                
                // Try to fetch additional details from the company's detail page
                await EnrichCompanyDetails(company);
            }
            else
            {
                _logger.LogWarning("No company found with name: '{CompanyName}'", name);
            }
            
            return company;
        }

        private async Task EnrichCompanyDetails(CompanyInfo company)
        {
            if (string.IsNullOrEmpty(company.DetailsUrl))
                return;

            try
            {
                _logger.LogInformation("Fetching additional details for {CompanyName} from {Url}", company.Name, company.DetailsUrl);
                
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var html = await client.GetStringAsync(company.DetailsUrl);
                
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // Look for ACN information
                var acnMatch = Regex.Match(html, @"ACN\s*:?\s*(\d{3}\s*\d{3}\s*\d{3})", RegexOptions.IgnoreCase);
                if (acnMatch.Success)
                {
                    company.ACN = acnMatch.Groups[1].Value.Trim();
                    _logger.LogInformation("Found ACN for {CompanyName}: {ACN}", company.Name, company.ACN);
                }
                
                // Look for additional company information
                var contentNode = doc.DocumentNode.SelectSingleNode("//main") ?? doc.DocumentNode;
                var additionalInfo = ExtractAdditionalInfo(contentNode.InnerText);
                if (!string.IsNullOrEmpty(additionalInfo))
                {
                    company.OtherDetails = $"{company.OtherDetails}\n{additionalInfo}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch additional details for {CompanyName} from {Url}", company.Name, company.DetailsUrl);
            }
        }

        private string ExtractAdditionalInfo(string text)
        {
            var info = new List<string>();
            
            // Look for various patterns of company information
            var patterns = new[]
            {
                @"ABN\s*:?\s*(\d{2}\s*\d{3}\s*\d{3}\s*\d{3})",
                @"Licence\s+number\s*:?\s*([A-Z0-9\-]+)",
                @"Status\s*:?\s*([A-Za-z\s]+)",
                @"Varied\s+on\s+([0-9\s\w,]+)",
                @"Last\s+varied\s+on\s+([0-9\s\w,]+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    info.Add($"{match.Groups[0].Value.Trim()}");
                }
            }

            return string.Join(", ", info);
        }
    }
}
